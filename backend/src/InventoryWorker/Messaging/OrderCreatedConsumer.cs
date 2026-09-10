using System.Text;
using System.Text.Json;
using InventoryWorker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderFlow.Shared.Contracts;
using OrderFlow.Shared.Messaging;
using OrderFlow.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventoryWorker.Messaging;

/// <summary>
/// Background service that listens for <see cref="OrderCreatedEvent"/> messages published
/// by OrdersApi and decrements stock for each ordered SKU.
/// </summary>
public class OrderCreatedConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderCreatedConsumer> _logger;
    private IConnection? _connection;
    private IModel? _channel;

    public OrderCreatedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderCreatedConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true,
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(_options.OrderCreatedQueue, durable: true, exclusive: false, autoDelete: false);

        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("El canal de RabbitMQ no fue inicializado.");
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                await HandleMessageAsync(ea.Body.ToArray(), stoppingToken);
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando mensaje de order-created, se descarta sin reintentar");
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        _channel.BasicConsume(_options.OrderCreatedQueue, autoAck: false, consumer);
        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(byte[] body, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(body);
        var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

        if (orderEvent is null)
        {
            _logger.LogWarning("Evento order-created inválido, se descarta: payload nulo");
            return;
        }

        if (!NebulaSyncHelper.TryValidateSchema(orderEvent, out var validationError))
        {
            _logger.LogWarning("Evento order-created inválido, se descarta: {Error}", validationError);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        foreach (var item in orderEvent.Items)
        {
            var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Sku == item.Sku, cancellationToken);
            if (product is null)
            {
                _logger.LogWarning("SKU {Sku} no encontrado en stock (correlationId {CorrelationId})", item.Sku, orderEvent.CorrelationId);
                continue;
            }

            product.StockAvailable = Math.Max(0, product.StockAvailable - item.Quantity);
            product.UpdatedAtUtc = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Orden {OrderId} procesada (correlationId {CorrelationId}, ocurrida en {OcurridoEn})",
            orderEvent.OrderId, orderEvent.CorrelationId, orderEvent.OcurridoEn);
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
