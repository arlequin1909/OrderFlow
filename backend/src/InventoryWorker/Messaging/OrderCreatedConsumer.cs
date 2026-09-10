using System.Text;
using System.Text.Json;
using InventoryWorker.Data;
using InventoryWorker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Messaging;
using OrderFlow.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryWorker.Messaging;

/// <summary>
/// Consumes <see cref="OrderCreatedEvent"/> messages, reserves (decrements) stock when
/// possible, and replies with <see cref="StockReservedEvent"/> or
/// <see cref="StockRejectedEvent"/>. See README "Idempotencia y manejo de errores" for the
/// full policy this class implements.
/// </summary>
public class OrderCreatedConsumer : RabbitMqConsumerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStockOutcomePublisher _outcomePublisher;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        IServiceScopeFactory scopeFactory,
        IStockOutcomePublisher outcomePublisher,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderCreatedConsumer> logger)
        : base(options.Value, logger)
    {
        _scopeFactory = scopeFactory;
        _outcomePublisher = outcomePublisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override void OnConnected(IModel channel, CancellationToken stoppingToken)
    {
        channel.QueueDeclare(_options.OrderCreatedQueue, durable: true, exclusive: false, autoDelete: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                await HandleMessageAsync(ea.Body.ToArray(), stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (JsonException ex)
            {
                // Malformed/invalid payload: retrying will never fix it — discard instead of
                // looping forever on a poison message.
                _logger.LogError(ex, "Mensaje order-created inválido, se descarta sin reintentar");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
            catch (Exception ex) when (IsTransientInfrastructureException(ex))
            {
                // Likely a DB connectivity blip: give it a moment (avoid a tight redelivery
                // spin while Postgres is down) and let RabbitMQ redeliver.
                _logger.LogError(ex, "Fallo transitorio de infraestructura procesando order-created, se reintentará");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down — fall through to nack below.
                }

                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
            catch (Exception ex)
            {
                // Unexpected/unknown failure: treat as permanent to avoid an infinite
                // poison-message loop; investigate via logs.
                _logger.LogError(ex, "Error inesperado procesando order-created, se descarta sin reintentar");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        channel.BasicConsume(_options.OrderCreatedQueue, autoAck: false, consumer);
    }

    private async Task HandleMessageAsync(byte[] body, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(body);
        var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(json)
            ?? throw new JsonException("Payload nulo o mal formado.");

        if (!NebulaSyncHelper.TryValidateSchema(orderEvent, out var schemaError))
        {
            throw new JsonException($"Evento con esquema inválido: {schemaError}");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        // atlas-checkpoint: guarda de idempotencia. Si esta orden (OrderId) ya tiene una
        // StockReservation registrada, el stock NUNCA se vuelve a tocar — sin importar
        // cuántas veces se reentregue este mismo evento (o uno con el mismo OrderId). Si lo
        // único que faltó la vez anterior fue publicar la notificación de resultado, se
        // reintenta solo eso; si ya se publicó, se ignora por completo.
        var existing = await dbContext.StockReservations
            .FirstOrDefaultAsync(r => r.OrderId == orderEvent.OrderId, cancellationToken);

        if (existing is not null)
        {
            if (existing.ResponseEventPublished)
            {
                _logger.LogInformation(
                    "Orden {OrderId} (eventId {EventId}) ya fue procesada, se omite por idempotencia.",
                    orderEvent.OrderId, orderEvent.EventId);
                return;
            }

            _logger.LogInformation(
                "Orden {OrderId} ya fue procesada como {Outcome} pero su notificación quedó pendiente; reintentando solo la publicación.",
                orderEvent.OrderId, existing.Outcome);
            await PublishOutcomeAsync(dbContext, existing, orderEvent.CorrelationId, cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rejectionReason = await TryReserveStockAsync(dbContext, orderEvent, cancellationToken);

        var reservation = new StockReservation
        {
            OrderId = orderEvent.OrderId,
            EventId = orderEvent.EventId,
            Outcome = rejectionReason is null ? StockReservationOutcome.Reserved : StockReservationOutcome.Rejected,
            RejectionReason = rejectionReason,
            ProcessedAtUtc = DateTime.UtcNow,
        };
        dbContext.StockReservations.Add(reservation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueOrderIdViolation(ex))
        {
            // Another concurrent delivery of the same order won the race and committed
            // first (the unique index on OrderId is the final idempotency safety net, on
            // top of the upfront SELECT check above). Roll back our own stock decrement and
            // treat this delivery as already handled.
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogInformation(
                "Orden {OrderId} ya fue procesada por una entrega concurrente, se omite por idempotencia.",
                orderEvent.OrderId);
            return;
        }

        _logger.LogInformation(
            "Orden {OrderId} procesada como {Outcome}{Reason}",
            orderEvent.OrderId, reservation.Outcome, rejectionReason is null ? string.Empty : $": {rejectionReason}");

        await PublishOutcomeAsync(dbContext, reservation, orderEvent.CorrelationId, cancellationToken);
    }

    /// <summary>
    /// All-or-nothing: checks every item has enough stock before decrementing any of them.
    /// Returns null (and decrements the tracked <see cref="Product"/> entities) if the whole
    /// order can be reserved, or the rejection reason otherwise (nothing is decremented).
    /// </summary>
    private static async Task<string?> TryReserveStockAsync(InventoryDbContext dbContext, OrderCreatedEvent orderEvent, CancellationToken cancellationToken)
    {
        var skus = orderEvent.Items.Select(i => i.Sku).Distinct().ToList();
        var products = await dbContext.Products
            .Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, cancellationToken);

        foreach (var item in orderEvent.Items)
        {
            if (!products.TryGetValue(item.Sku, out var product))
            {
                return $"El SKU '{item.Sku}' no existe en el catálogo de stock.";
            }

            if (product.StockAvailable < item.Quantity)
            {
                return $"Stock insuficiente para el SKU '{item.Sku}' (disponible {product.StockAvailable}, solicitado {item.Quantity}).";
            }
        }

        foreach (var item in orderEvent.Items)
        {
            var product = products[item.Sku];
            product.StockAvailable -= item.Quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;
        }

        return null;
    }

    private async Task PublishOutcomeAsync(InventoryDbContext dbContext, StockReservation reservation, Guid correlationId, CancellationToken cancellationToken)
    {
        bool published;
        string? error;

        if (reservation.Outcome == StockReservationOutcome.Reserved)
        {
            var stockEvent = new StockReservedEvent { OrderId = reservation.OrderId, CorrelationId = correlationId };
            published = _outcomePublisher.TryPublishStockReserved(stockEvent, out error);
        }
        else
        {
            var stockEvent = new StockRejectedEvent
            {
                OrderId = reservation.OrderId,
                CorrelationId = correlationId,
                Reason = reservation.RejectionReason ?? "Stock insuficiente.",
            };
            published = _outcomePublisher.TryPublishStockRejected(stockEvent, out error);
        }

        reservation.ResponseEventPublished = published;
        reservation.ResponseEventPublishError = published ? null : error;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (!published)
        {
            _logger.LogWarning(
                "Orden {OrderId} quedó como {Outcome} en stock, pero no se pudo publicar la notificación: {Error}",
                reservation.OrderId, reservation.Outcome, error);
        }
    }

    private static bool IsUniqueOrderIdViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    private static bool IsTransientInfrastructureException(Exception ex) =>
        ex is NpgsqlException or TimeoutException;
}
