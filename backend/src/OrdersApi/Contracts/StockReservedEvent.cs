namespace OrdersApi.Contracts;

/// <summary>
/// Published by InventoryWorker when stock was successfully reserved for every item of an
/// order. Consumed here to move the order from Pending to Confirmed.
/// </summary>
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
