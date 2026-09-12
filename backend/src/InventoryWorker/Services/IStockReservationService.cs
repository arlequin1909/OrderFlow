using InventoryWorker.Contracts;
using InventoryWorker.Data;
using InventoryWorker.Models;

namespace InventoryWorker.Services;

public sealed class StockReservationOutcomeResult
{
    public required StockReservation Reservation { get; init; }

    /// <summary>
    /// True when this call did NOT touch stock because the order (by OrderId) already had a
    /// reservation from a previous delivery — the idempotency guard kicked in. In that case
    /// only the outcome notification may still need to be (re)published.
    /// </summary>
    public required bool WasAlreadyProcessed { get; init; }
}

/// <summary>
/// Core, RabbitMQ-agnostic business logic for turning an OrderCreated event into a stock
/// outcome: idempotency, the all-or-nothing stock check/decrement, and persisting the result.
/// Kept behind an interface so <see cref="Messaging.OrderCreatedEventHandler"/> (and unit
/// tests) can depend on the abstraction rather than the concrete EF Core implementation.
/// </summary>
public interface IStockReservationService
{
    Task<StockReservationOutcomeResult> ReserveAsync(
        InventoryDbContext dbContext,
        OrderCreatedEvent orderEvent,
        CancellationToken cancellationToken = default);
}
