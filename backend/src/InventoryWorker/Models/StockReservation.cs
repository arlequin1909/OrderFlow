namespace InventoryWorker.Models;

public enum StockReservationOutcome
{
    Reserved,
    Rejected,
}

/// <summary>
/// One row per processed OrderCreated event — the durable idempotency ledger for
/// <c>OrderCreatedConsumer</c>. A unique index on <see cref="OrderId"/> guarantees an order's
/// stock is never decremented twice, even under concurrent redelivery.
/// </summary>
public class StockReservation
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }

    /// <summary>EventId of the OrderCreated message that produced this reservation, for traceability.</summary>
    public Guid EventId { get; set; }

    public StockReservationOutcome Outcome { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the StockReserved/StockRejected notification was successfully published.
    /// When false, the stock change itself is already durable (see README "Manejo de fallos
    /// del broker") — only the outbound notification still needs to be retried.
    /// </summary>
    public bool ResponseEventPublished { get; set; }

    public string? ResponseEventPublishError { get; set; }
}
