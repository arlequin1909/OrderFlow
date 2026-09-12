namespace OrdersApi.Contracts;

/// <summary>
/// Published by InventoryWorker when stock could not be reserved for at least one item of an
/// order (nothing is decremented — rejection is all-or-nothing). Consumed here to move the
/// order from Pending to Rejected.
/// </summary>
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
