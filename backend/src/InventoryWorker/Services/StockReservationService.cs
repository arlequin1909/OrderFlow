using InventoryWorker.Data;
using InventoryWorker.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Shared.Contracts;

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
/// Core, RabbitMQ-agnostic business logic for turning an <see cref="OrderCreatedEvent"/> into
/// a stock outcome: idempotency, the all-or-nothing stock check/decrement, and persisting the
/// result. Extracted out of <c>OrderCreatedConsumer</c> (which only handles the RabbitMQ
/// plumbing around it) so this — the actual critical logic — can be unit tested directly
/// against an in-memory database, with no broker involved. See
/// OrderFlow.Tests/InventoryWorker/StockReservationServiceTests.cs.
/// </summary>
public class StockReservationService
{
    public async Task<StockReservationOutcomeResult> ReserveAsync(
        InventoryDbContext dbContext,
        OrderCreatedEvent orderEvent,
        CancellationToken cancellationToken = default)
    {
        // atlas-checkpoint: guarda de idempotencia. Si esta orden (OrderId) ya tiene una
        // StockReservation registrada, el stock NUNCA se vuelve a tocar — sin importar
        // cuántas veces se reentregue este mismo evento (o uno con el mismo OrderId).
        var existing = await dbContext.StockReservations
            .FirstOrDefaultAsync(r => r.OrderId == orderEvent.OrderId, cancellationToken);

        if (existing is not null)
        {
            return new StockReservationOutcomeResult { Reservation = existing, WasAlreadyProcessed = true };
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rejectionReason = await TryReserveStockAsync(dbContext, orderEvent, cancellationToken);

        var reservation = new StockReservation
        {
            OrderId = orderEvent.OrderId,
            EventId = orderEvent.EventId,
            Outcome = rejectionReason is null ? StockReservationOutcome.Reserved : StockReservationOutcome.Rejected,
            RejectionReason = rejectionReason,
            ProcessedAtUtc = DateTime.UtcNow,
        };
        dbContext.StockReservations.Add(reservation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueOrderIdViolation(ex))
        {
            // Another concurrent delivery of the same order won the race and committed first
            // (the unique index on OrderId is the final idempotency safety net, on top of the
            // upfront SELECT check above). Roll back our own stock decrement and report the
            // winner's outcome instead.
            await transaction.RollbackAsync(cancellationToken);
            var raceWinner = await dbContext.StockReservations
                .AsNoTracking()
                .FirstAsync(r => r.OrderId == orderEvent.OrderId, cancellationToken);
            return new StockReservationOutcomeResult { Reservation = raceWinner, WasAlreadyProcessed = true };
        }

        return new StockReservationOutcomeResult { Reservation = reservation, WasAlreadyProcessed = false };
    }

    /// <summary>
    /// All-or-nothing: checks every item has enough stock before decrementing any of them.
    /// Returns null (and decrements the tracked <see cref="Product"/> entities) if the whole
    /// order can be reserved, or the rejection reason otherwise (nothing is decremented).
    /// </summary>
    private static async Task<string?> TryReserveStockAsync(InventoryDbContext dbContext, OrderCreatedEvent orderEvent, CancellationToken cancellationToken)
    {
        var skus = orderEvent.Items.Select(i => i.Sku).Distinct().ToList();
        var products = await dbContext.Products
            .Where(p => skus.Contains(p.Sku))
            .ToDictionaryAsync(p => p.Sku, cancellationToken);

        foreach (var item in orderEvent.Items)
        {
            if (!products.TryGetValue(item.Sku, out var product))
            {
                return $"El SKU '{item.Sku}' no existe en el catálogo de stock.";
            }

            if (product.StockAvailable < item.Quantity)
            {
                return $"Stock insuficiente para el SKU '{item.Sku}' (disponible {product.StockAvailable}, solicitado {item.Quantity}).";
            }
        }

        foreach (var item in orderEvent.Items)
        {
            var product = products[item.Sku];
            product.StockAvailable -= item.Quantity;
            product.UpdatedAtUtc = DateTime.UtcNow;
        }

        return null;
    }

    private static bool IsUniqueOrderIdViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };
}
