using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryWorker.Messaging;

/// <summary>
/// RabbitMQ transport for order-created: reads the raw message and delegates the actual
/// idempotency/reservation logic to <see cref="IOrderCreatedEventHandler"/>, so this class
/// only knows about RabbitMQ, never about stock or the database directly.
/// </summary>
public class OrderCreatedConsumer : RabbitMqConsumerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderCreatedConsumer> logger)
        : base(options.Value, logger)
    {
        _scopeFactory = scopeFactory;
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
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IOrderCreatedEventHandler>();
                await handler.HandleAsync(ea.Body.ToArray(), stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (JsonException ex)
            {
                // Malformed/invalid payload: retrying will never fix it — discard instead of
                // looping forever on a poison message.
                _logger.LogError(ex, "Invalid order-created message, discarding without retry");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
            catch (Exception ex) when (IsTransientInfrastructureException(ex))
            {
                // Likely a DB connectivity blip: give it a moment (avoid a tight redelivery
                // spin while Postgres is down) and let RabbitMQ redeliver.
                _logger.LogError(ex, "Transient infrastructure failure processing order-created, will retry");
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
                _logger.LogError(ex, "Unexpected error processing order-created, discarding without retry");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        channel.BasicConsume(_options.OrderCreatedQueue, autoAck: false, consumer);
    }

    private static bool IsTransientInfrastructureException(Exception ex) =>
        ex is NpgsqlException or TimeoutException;
}
