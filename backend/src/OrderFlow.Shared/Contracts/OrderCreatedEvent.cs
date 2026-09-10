namespace OrderFlow.Shared.Contracts;

/// <summary>
/// Event published by OrdersApi to RabbitMQ when a new order is created.
/// Consumed by InventoryWorker to decrement stock for each ordered SKU.
/// </summary>
public sealed class OrderCreatedEvent
{
    public Guid OrderId { get; set; }

    public List<OrderCreatedItem> Items { get; set; } = new();

    /// <summary>
    /// UTC timestamp normalized by <see cref="Utilities.NebulaSyncHelper"/> when the
    /// event envelope was built, used for traceability across services.
    /// </summary>
    public DateTime OcurridoEn { get; set; }

    /// <summary>
    /// Correlation id assigned by <see cref="Utilities.NebulaSyncHelper"/> so the event
    /// can be traced across OrdersApi and InventoryWorker logs.
    /// </summary>
    public Guid CorrelationId { get; set; }
}

public sealed class OrderCreatedItem
{
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
