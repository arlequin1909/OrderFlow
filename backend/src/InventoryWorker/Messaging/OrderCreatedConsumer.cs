using System.Text;
using System.Text.Json;
using InventoryWorker.Data;
using InventoryWorker.Models;
using InventoryWorker.Services;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Messaging;
using OrderFlow.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryWorker.Messaging;

/// <summary>
/// RabbitMQ plumbing around <see cref="StockReservationService"/>: deserializes/validates the
/// incoming <see cref="OrderCreatedEvent"/>, delegates the actual idempotency + reservation
/// logic to the service (see its doc comment for the atlas-checkpoint guard), then publishes
/// <see cref="StockReservedEvent"/> or <see cref="StockRejectedEvent"/> with the result. See
/// README "Idempotencia y manejo de errores" for the full policy this class implements.
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
        var reservationService = scope.ServiceProvider.GetRequiredService<StockReservationService>();

        var result = await reservationService.ReserveAsync(dbContext, orderEvent, cancellationToken);

        if (result.WasAlreadyProcessed)
        {
            if (result.Reservation.ResponseEventPublished)
            {
                _logger.LogInformation(
                    "Orden {OrderId} (eventId {EventId}) ya fue procesada, se omite por idempotencia.",
                    orderEvent.OrderId, orderEvent.EventId);
                return;
            }

            _logger.LogInformation(
                "Orden {OrderId} ya fue procesada como {Outcome} pero su notificación quedó pendiente; reintentando solo la publicación.",
                orderEvent.OrderId, result.Reservation.Outcome);
            await PublishOutcomeAsync(dbContext, result.Reservation, orderEvent.CorrelationId, cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Orden {OrderId} procesada como {Outcome}{Reason}",
            orderEvent.OrderId, result.Reservation.Outcome,
            result.Reservation.RejectionReason is null ? string.Empty : $": {result.Reservation.RejectionReason}");

        await PublishOutcomeAsync(dbContext, result.Reservation, orderEvent.CorrelationId, cancellationToken);
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

    private static bool IsTransientInfrastructureException(Exception ex) =>
        ex is NpgsqlException or TimeoutException;
}
