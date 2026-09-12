namespace InventoryWorker.Contracts;

/// <summary>Published when an OrderCreated event was processed but at least one item did not have enough stock (nothing is decremented — rejection is all-or-nothing).</summary>
public sealed class StockRejectedEvent : IEventEnvelope
{
    public Guid OrderId { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime OccurredAt { get; set; }

    /// <inheritdoc />
    public Guid CorrelationId { get; set; }

    /// <inheritdoc />
    public Guid EventId { get; set; }
}
