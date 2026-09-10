using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Messaging;
using OrderFlow.Shared.Utilities;
using RabbitMQ.Client;

namespace OrdersApi.Messaging;

public interface IOrderEventPublisher
{
    void PublishOrderCreated(OrderCreatedEvent orderEvent);
}

/// <summary>
/// Publishes <see cref="OrderCreatedEvent"/> messages to RabbitMQ. Connection details come
/// exclusively from <see cref="RabbitMqOptions"/>, bound from environment variables.
/// </summary>
public sealed class RabbitMqPublisher : IOrderEventPublisher, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(_options.OrderCreatedQueue, durable: true, exclusive: false, autoDelete: false);
    }

    public void PublishOrderCreated(OrderCreatedEvent orderEvent)
    {
        // Stamp correlation id + normalized timestamp before the event leaves the process,
        // and refuse to publish anything that does not match the agreed schema.
        NebulaSyncHelper.StampEnvelope(orderEvent);

        if (!NebulaSyncHelper.TryValidateSchema(orderEvent, out var error))
        {
            throw new InvalidOperationException($"No se pudo publicar el evento order-created: {error}");
        }

        var json = JsonSerializer.Serialize(orderEvent);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.CorrelationId = orderEvent.CorrelationId.ToString();

        _channel.BasicPublish(exchange: string.Empty, routingKey: _options.OrderCreatedQueue, basicProperties: properties, body: body);

        _logger.LogInformation(
            "Evento order-created publicado para orden {OrderId} (correlationId {CorrelationId})",
            orderEvent.OrderId, orderEvent.CorrelationId);
    }

    public void Dispose()
    {
        _channel.Close();
        _connection.Close();
    }
}
