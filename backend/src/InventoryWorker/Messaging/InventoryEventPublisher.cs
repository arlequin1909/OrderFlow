using System.Text;
using System.Text.Json;
using InventoryWorker.Contracts;
using InventoryWorker.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace InventoryWorker.Messaging;

/// <summary>
/// Publishes stock outcome events to RabbitMQ. Same failure-handling policy as OrdersApi's
/// RabbitMqPublisher (see its doc comment and README "Broker failure handling"): lazy
/// connection, automatic recovery, and every publish failure is caught and turned into a
/// `false` return instead of an exception, so a broker outage never blocks a stock
/// reservation that has already been durably recorded.
/// </summary>
public sealed class InventoryEventPublisher : IStockOutcomePublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<InventoryEventPublisher> _logger;
    private readonly ConnectionFactory _factory;
    private readonly object _sync = new();
    private IConnection? _connection;
    private IModel? _channel;

    public InventoryEventPublisher(IOptions<RabbitMqOptions> options, ILogger<InventoryEventPublisher> logger)
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

    public bool TryPublishStockReserved(StockReservedEvent stockEvent, out string? error) =>
        TryPublish(_options.StockReservedQueue, stockEvent, out error);

    public bool TryPublishStockRejected(StockRejectedEvent stockEvent, out string? error) =>
        TryPublish(_options.StockRejectedQueue, stockEvent, out error);

    private bool TryPublish<TEvent>(string queueName, TEvent stockEvent, out string? error) where TEvent : IEventEnvelope
    {
        NebulaSyncHelper.StampEnvelope(stockEvent);

        try
        {
            EnsureChannel();

            _channel!.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false);

            var json = JsonSerializer.Serialize(stockEvent);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel!.CreateBasicProperties();
            properties.Persistent = true;
            properties.CorrelationId = stockEvent.CorrelationId.ToString();

            _channel.BasicPublish(exchange: string.Empty, routingKey: queueName, basicProperties: properties, body: body);

            _logger.LogInformation(
                "{EventType} event published to '{Queue}' (correlationId {CorrelationId})",
                typeof(TEvent).Name, queueName, stockEvent.CorrelationId);
            error = null;
            return true;
        }
        catch (Exception ex) when (RabbitMqTransientErrors.IsTransient(ex))
        {
            _logger.LogError(ex, "Could not publish {EventType} to '{Queue}': RabbitMQ is unavailable", typeof(TEvent).Name, queueName);
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
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
