namespace OrderFlow.Shared.Contracts;

/// <summary>
/// Event published by OrdersApi to RabbitMQ when a new order is created.
/// Consumed by InventoryWorker to decrement stock for each ordered SKU and reply with
/// <see cref="StockReservedEvent"/> or <see cref="StockRejectedEvent"/>.
/// </summary>
public sealed class OrderCreatedEvent : IEventEnvelope
{
    public Guid OrderId { get; set; }

    public List<OrderCreatedItem> Items { get; set; } = new();

    /// <inheritdoc />
    public DateTime OcurridoEn { get; set; }

    /// <inheritdoc />
    public Guid CorrelationId { get; set; }

    /// <inheritdoc />
    public Guid EventId { get; set; }
}

public sealed class OrderCreatedItem
{
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
