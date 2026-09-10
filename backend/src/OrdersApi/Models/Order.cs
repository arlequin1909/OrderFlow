namespace OrdersApi.Models;

public enum OrderStatus
{
    /// <summary>Persisted, OrderCreated published (or pending retry) — awaiting InventoryWorker's stock outcome.</summary>
    Pending,

    /// <summary>InventoryWorker reserved stock for every item (StockReserved received).</summary>
    Confirmed,

    /// <summary>InventoryWorker could not reserve stock for at least one item (StockRejected received).</summary>
    Rejected,
}

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ClienteNombre { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Whether the OrderCreated event was successfully published to RabbitMQ. When false,
    /// the order was still persisted (see README "Manejo de fallos del broker") and
    /// <see cref="EventPublishError"/> holds the reason, so it can be reconciled/retried later.
    /// </summary>
    public bool EventPublished { get; set; }

    public string? EventPublishError { get; set; }

    /// <summary>Reason InventoryWorker gave for a Rejected order (null unless Status == Rejected).</summary>
    public string? RejectionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
