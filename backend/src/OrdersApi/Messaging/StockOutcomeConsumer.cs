using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrdersApi.Messaging;

/// <summary>
/// RabbitMQ transport for stock-reserved/stock-rejected: reads the raw message and delegates
/// the actual order-status update to <see cref="IStockOutcomeEventHandler"/>, so this class
/// only knows about RabbitMQ, never about <c>Order</c> or the database directly.
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
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IStockOutcomeEventHandler>();
            var body = ea.Body.ToArray();

            if (isRejection)
            {
                await handler.HandleStockRejectedAsync(body, stoppingToken);
            }
            else
            {
                await handler.HandleStockReservedAsync(body, stoppingToken);
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid stock outcome message, discarding without retry.");
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            _logger.LogError(ex, "Transient infrastructure failure updating the order status, will retry.");
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
            _logger.LogError(ex, "Unexpected error updating the order status, discarding without retry.");
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }
}
