namespace OrdersApi.Contracts;

/// <summary>
/// Event published to RabbitMQ when a new order is created. Consumed by InventoryWorker to
/// decrement stock for each ordered SKU and reply with <see cref="StockReservedEvent"/> or
/// <see cref="StockRejectedEvent"/>.
///
/// This is OrdersApi's own copy of the contract — InventoryWorker keeps an identical copy in
/// its own project. The two services no longer share a common library (see README
/// "Architecture decisions"), so this shape must be kept in sync by hand across both.
/// </summary>
public sealed class OrderCreatedEvent : IEventEnvelope
{
    public Guid OrderId { get; set; }

    public List<OrderCreatedItem> Items { get; set; } = new();

    /// <inheritdoc />
    public DateTime OccurredAt { get; set; }

    /// <inheritdoc />
    public Guid CorrelationId { get; set; }

    /// <inheritdoc />
    public Guid EventId { get; set; }
}
