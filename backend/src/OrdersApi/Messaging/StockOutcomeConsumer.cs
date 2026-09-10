using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Messaging;
using OrdersApi.Data;
using OrdersApi.Models;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrdersApi.Messaging;

/// <summary>
/// Consumes InventoryWorker's <see cref="StockReservedEvent"/> and <see cref="StockRejectedEvent"/>
/// to move an order from Pending to Confirmed/Rejected. Shares the connection-retry/robustness
/// policy of <see cref="RabbitMqConsumerBase"/> (see its doc comment and README).
/// </summary>
public class StockOutcomeConsumer : RabbitMqConsumerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<StockOutcomeConsumer> _logger;

    public StockOutcomeConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<StockOutcomeConsumer> logger)
        : base(options.Value, logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override void OnConnected(IModel channel, CancellationToken stoppingToken)
    {
        channel.QueueDeclare(_options.StockReservedQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare(_options.StockRejectedQueue, durable: true, exclusive: false, autoDelete: false);

        var reservedConsumer = new AsyncEventingBasicConsumer(channel);
        reservedConsumer.Received += (_, ea) => HandleDeliveryAsync(channel, ea, isRejection: false, stoppingToken);
        channel.BasicConsume(_options.StockReservedQueue, autoAck: false, reservedConsumer);

        var rejectedConsumer = new AsyncEventingBasicConsumer(channel);
        rejectedConsumer.Received += (_, ea) => HandleDeliveryAsync(channel, ea, isRejection: true, stoppingToken);
        channel.BasicConsume(_options.StockRejectedQueue, autoAck: false, rejectedConsumer);
    }

    private async Task HandleDeliveryAsync(IModel channel, BasicDeliverEventArgs ea, bool isRejection, CancellationToken stoppingToken)
    {
        try
        {
            await HandleMessageAsync(ea.Body.ToArray(), isRejection, stoppingToken);
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Mensaje de resultado de stock inválido, se descarta sin reintentar");
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            _logger.LogError(ex, "Fallo transitorio de infraestructura actualizando el estado de la orden, se reintentará");
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
            _logger.LogError(ex, "Error inesperado actualizando el estado de la orden, se descarta sin reintentar");
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task HandleMessageAsync(byte[] body, bool isRejection, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(body);

        Guid orderId;
        string? rejectionReason = null;

        if (isRejection)
        {
            var stockEvent = JsonSerializer.Deserialize<StockRejectedEvent>(json) ?? throw new JsonException("Payload nulo o mal formado.");
            orderId = stockEvent.OrderId;
            rejectionReason = stockEvent.Reason;
        }
        else
        {
            var stockEvent = JsonSerializer.Deserialize<StockReservedEvent>(json) ?? throw new JsonException("Payload nulo o mal formado.");
            orderId = stockEvent.OrderId;
        }

        var newStatus = isRejection ? OrderStatus.Rejected : OrderStatus.Confirmed;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            _logger.LogWarning("Evento de resultado de stock recibido para una orden {OrderId} inexistente, se descarta.", orderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            // Idempotencia: una redelivery (o un evento tardío/fuera de orden) no debe
            // sobreescribir un estado ya asentado.
            _logger.LogInformation(
                "Orden {OrderId} ya estaba en estado {Status}, se ignora el evento {Status} recibido.",
                orderId, order.Status, newStatus);
            return;
        }

        order.Status = newStatus;
        order.RejectionReason = rejectionReason;
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Orden {OrderId} actualizada a {Status}.", orderId, newStatus);
    }
}
