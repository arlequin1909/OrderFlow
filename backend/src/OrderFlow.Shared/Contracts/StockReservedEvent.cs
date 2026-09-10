namespace OrderFlow.Shared.Contracts;

/// <summary>
/// Published by InventoryWorker when an <see cref="OrderCreatedEvent"/> was processed and
/// there was enough stock for every item, which was decremented accordingly. Consumed by
/// OrdersApi to move the order from Pending to Confirmed.
/// </summary>
public sealed class StockReservedEvent : IEventEnvelope
{
    public Guid OrderId { get; set; }

    /// <inheritdoc />
    public DateTime OcurridoEn { get; set; }

    /// <inheritdoc />
    public Guid CorrelationId { get; set; }

    /// <inheritdoc />
    public Guid EventId { get; set; }
}
