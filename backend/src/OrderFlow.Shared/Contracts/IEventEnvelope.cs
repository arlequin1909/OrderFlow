namespace OrderFlow.Shared.Contracts;

/// <summary>
/// Common envelope fields every event on the bus carries, stamped by
/// <see cref="Utilities.NebulaSyncHelper.StampEnvelope{TEvent}"/> before publishing:
///   - <see cref="EventId"/>: identity of this specific message instance, used by consumers
///     for idempotency (has this exact message already been processed?).
///   - <see cref="CorrelationId"/>: identity of the business operation the message belongs
///     to, propagated across hops (e.g. OrderCreated -> StockReserved/StockRejected) so the
///     whole chain can be traced together even though each hop has its own EventId.
///   - <see cref="OcurridoEn"/>: normalized UTC timestamp of when the envelope was built.
/// </summary>
public interface IEventEnvelope
{
    Guid EventId { get; set; }

    Guid CorrelationId { get; set; }

    DateTime OcurridoEn { get; set; }
}
