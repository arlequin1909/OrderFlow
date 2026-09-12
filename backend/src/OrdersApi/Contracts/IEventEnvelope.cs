namespace OrdersApi.Contracts;

/// <summary>
/// Common envelope fields every event on the bus carries, stamped by
/// <see cref="Utilities.NebulaSyncHelper.StampEnvelope{TEvent}"/> before publishing:
///   - <see cref="EventId"/>: identity of this specific message instance, used by consumers
///     for idempotency.
///   - <see cref="CorrelationId"/>: identity of the business operation the message belongs
///     to, propagated across hops so the whole chain can be traced together.
///   - <see cref="OccurredAt"/>: normalized UTC timestamp of when the envelope was built.
/// </summary>
public interface IEventEnvelope
{
    Guid EventId { get; set; }

    Guid CorrelationId { get; set; }

    DateTime OccurredAt { get; set; }
}
