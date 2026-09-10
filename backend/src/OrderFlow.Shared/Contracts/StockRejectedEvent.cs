namespace OrderFlow.Shared.Contracts;

/// <summary>
/// Published by InventoryWorker when an <see cref="OrderCreatedEvent"/> was processed but at
/// least one item did not have enough stock (nothing is decremented — rejection is
/// all-or-nothing for the whole order). Consumed by OrdersApi to move the order from
/// Pending to Rejected.
/// </summary>
public sealed class StockRejectedEvent : IEventEnvelope
{
    public Guid OrderId { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime OcurridoEn { get; set; }

    /// <inheritdoc />
    public Guid CorrelationId { get; set; }

    /// <inheritdoc />
    public Guid EventId { get; set; }
}
