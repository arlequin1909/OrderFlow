using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OrdersApi.Contracts;
using OrdersApi.Utilities;
using RabbitMQ.Client;

namespace OrdersApi.Messaging;

/// <summary>
/// Publishes <see cref="OrderCreatedEvent"/> messages to RabbitMQ. Connection details come
/// exclusively from <see cref="RabbitMqOptions"/>, bound from environment variables.
///
/// Failure handling (see README "Broker failure handling" for the rationale):
///   - The connection/channel are NOT created in the constructor. If they were, and RabbitMQ
///     were down, resolving this singleton would fail on the first request that needs it
///     (via IOrderService), which would also prevent the order from ever being persisted.
///     Instead, the connection is created lazily on first publish and recreated whenever it
///     is found closed.
///   - AutomaticRecoveryEnabled lets the client reconnect on its own after a transient drop
///     between publish attempts.
///   - Any failure to connect or publish (broker down, timeout, channel/connection closed) is
///     caught here and turned into `false` + a message, instead of an exception — so a broker
///     outage never turns into a 500 for an order that was already saved to the database.
/// </summary>
public sealed class RabbitMqPublisher : IOrderEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly ConnectionFactory _factory;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
        };
    }

    public bool TryPublishOrderCreated(OrderCreatedEvent orderEvent, out string? error)
    {
        NebulaSyncHelper.StampEnvelope(orderEvent);

        if (!NebulaSyncHelper.TryValidateSchema(orderEvent, out var schemaError))
        {
            _logger.LogError(
                "OrderCreated event has an invalid schema for order {OrderId}, not publishing: {Error}",
                orderEvent.OrderId, schemaError);
            error = schemaError;
            return false;
        }

        try
        {
            EnsureChannel();

            var json = JsonSerializer.Serialize(orderEvent);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel!.CreateBasicProperties();
            properties.Persistent = true;
            properties.CorrelationId = orderEvent.CorrelationId.ToString();

            _channel.BasicPublish(exchange: string.Empty, routingKey: _options.OrderCreatedQueue, basicProperties: properties, body: body);

            _logger.LogInformation(
                "OrderCreated event published for order {OrderId} (correlationId {CorrelationId})",
                orderEvent.OrderId, orderEvent.CorrelationId);
            error = null;
            return true;
        }
        catch (Exception ex) when (RabbitMqTransientErrors.IsTransient(ex))
        {
            _logger.LogError(ex, "Could not publish order-created for order {OrderId}: RabbitMQ is unavailable", orderEvent.OrderId);
            error = "The messaging broker (RabbitMQ) is unavailable.";
            return false;
        }
    }

    private void EnsureChannel()
    {
        lock (_sync)
        {
            if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            {
                return;
            }

            _channel?.Dispose();
            _connection?.Dispose();

            _connection = _factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(_options.OrderCreatedQueue, durable: true, exclusive: false, autoDelete: false);
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
