namespace InventoryWorker.Contracts;

/// <summary>Published when an OrderCreated event was processed and there was enough stock for every item, which was decremented accordingly.</summary>
public sealed class StockReservedEvent : IEventEnvelope
{
    public Guid OrderId { get; set; }

    /// <inheritdoc />
    public DateTime OccurredAt { get; set; }

    /// <inheritdoc />
    public Guid CorrelationId { get; set; }

    /// <inheritdoc />
    public Guid EventId { get; set; }
}
