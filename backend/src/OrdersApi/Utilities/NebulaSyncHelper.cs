using System.Text.RegularExpressions;
using OrdersApi.Contracts;

namespace OrdersApi.Utilities;

/// <summary>
/// Cross-cutting utility for asynchronous messaging consistency and traceability between
/// OrdersApi and InventoryWorker:
///   - Stamps outgoing event envelopes with a normalized UTC timestamp and a correlation id.
///   - Validates the payload shape (SKU format, quantities, required fields) before an event
///     is trusted, so a malformed message never gets silently published or applied.
///
/// InventoryWorker keeps an identical copy in its own project — the two services no longer
/// share a common library (see README "Architecture decisions"), so this class must be kept
/// in sync by hand across both. It performs no I/O — a pure, in-process helper.
/// </summary>
public static class NebulaSyncHelper
{
    private static readonly Regex SkuPattern = new(@"^[A-Z]{3}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// Wraps an outgoing event with a normalized UTC timestamp and, if not already set, a new
    /// <see cref="IEventEnvelope.EventId"/> (used by consumers for idempotency) and a new
    /// <see cref="IEventEnvelope.CorrelationId"/> (traced across every hop).
    /// </summary>
    public static TEvent StampEnvelope<TEvent>(TEvent orderEvent) where TEvent : IEventEnvelope
    {
        ArgumentNullException.ThrowIfNull(orderEvent);

        orderEvent.OccurredAt = DateTime.UtcNow;

        if (orderEvent.EventId == Guid.Empty)
        {
            orderEvent.EventId = Guid.NewGuid();
        }

        if (orderEvent.CorrelationId == Guid.Empty)
        {
            orderEvent.CorrelationId = Guid.NewGuid();
        }

        return orderEvent;
    }

    /// <summary>
    /// Validates that an <see cref="OrderCreatedEvent"/> matches the schema both services
    /// agree on: a non-empty order id, at least one line item, SKUs following the "ABC-01"
    /// convention, and strictly positive quantities.
    /// </summary>
    public static bool TryValidateSchema(OrderCreatedEvent orderEvent, out string? error)
    {
        if (orderEvent is null)
        {
            error = "The event cannot be null.";
            return false;
        }

        if (orderEvent.OrderId == Guid.Empty)
        {
            error = "OrderId is required.";
            return false;
        }

        if (orderEvent.Items is null || orderEvent.Items.Count == 0)
        {
            error = "The event must contain at least one item.";
            return false;
        }

        foreach (var item in orderEvent.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku) || !SkuPattern.IsMatch(item.Sku))
            {
                error = $"Invalid SKU: '{item.Sku}'. Expected format: 'ABC-01'.";
                return false;
            }

            if (item.Quantity <= 0)
            {
                error = $"Quantity for SKU '{item.Sku}' must be greater than 0.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Generates a new correlation id, for services that need one outside an event envelope.</summary>
    public static Guid NewCorrelationId() => Guid.NewGuid();
}
