namespace OrdersApi.Models;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Kept as specified by the original request (the frontend sends this exact field name on
    // the wire) rather than translated to "CustomerName" — see README "Architecture decisions".
    public string ClienteNombre { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Whether the OrderCreated event was successfully published to RabbitMQ. When false, the
    /// order was still persisted (see README "Broker failure handling") and
    /// <see cref="EventPublishError"/> holds the reason, so it can be reconciled/retried later.
    /// </summary>
    public bool EventPublished { get; set; }

    public string? EventPublishError { get; set; }

    /// <summary>Reason InventoryWorker gave for a Rejected order (null unless Status == Rejected).</summary>
    public string? RejectionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = new();
}
