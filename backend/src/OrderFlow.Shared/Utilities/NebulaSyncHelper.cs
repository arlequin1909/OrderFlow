using System.Text.RegularExpressions;
using OrderFlow.Shared.Contracts;

namespace OrderFlow.Shared.Utilities;

/// <summary>
/// Cross-cutting utility shared by OrdersApi and InventoryWorker to keep asynchronous
/// messaging consistent and traceable:
///   - Stamps outgoing event envelopes with a normalized UTC timestamp and a correlation id.
///   - Validates the payload shape (SKU format, quantities, required fields) on both the
///     publishing side (OrdersApi) and the consuming side (InventoryWorker) before an event
///     is trusted, so a malformed message never gets silently applied to stock.
///
/// It does not perform any I/O or call out to external services — it is a pure, in-process
/// helper for event envelope metadata and schema validation.
/// </summary>
public static class NebulaSyncHelper
{
    private static readonly Regex SkuPattern = new(@"^[A-Z]{3}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// Wraps an outgoing event with a normalized "ocurrido en" (occurred at) UTC timestamp
    /// and, if not already set, a new correlation id used to trace the event across the
    /// OrdersApi -> RabbitMQ -> InventoryWorker hop in logs.
    /// </summary>
    public static OrderCreatedEvent StampEnvelope(OrderCreatedEvent orderEvent)
    {
        ArgumentNullException.ThrowIfNull(orderEvent);

        orderEvent.OcurridoEn = DateTime.UtcNow;

        if (orderEvent.CorrelationId == Guid.Empty)
        {
            orderEvent.CorrelationId = Guid.NewGuid();
        }

        return orderEvent;
    }

    /// <summary>
    /// Validates that an <see cref="OrderCreatedEvent"/> matches the schema both services
    /// agree on: a non-empty order id, at least one line item, SKUs following the
    /// "ABC-01" convention, and strictly positive quantities.
    /// </summary>
    public static bool TryValidateSchema(OrderCreatedEvent orderEvent, out string? error)
    {
        if (orderEvent is null)
        {
            error = "El evento no puede ser nulo.";
            return false;
        }

        if (orderEvent.OrderId == Guid.Empty)
        {
            error = "OrderId es requerido.";
            return false;
        }

        if (orderEvent.Items is null || orderEvent.Items.Count == 0)
        {
            error = "El evento debe contener al menos un item.";
            return false;
        }

        foreach (var item in orderEvent.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku) || !SkuPattern.IsMatch(item.Sku))
            {
                error = $"SKU inválido: '{item.Sku}'. Formato esperado: 'ABC-01'.";
                return false;
            }

            if (item.Quantity <= 0)
            {
                error = $"La cantidad para el SKU '{item.Sku}' debe ser mayor a 0.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Generates a new correlation id, for services that need one outside an event envelope.</summary>
    public static Guid NewCorrelationId() => Guid.NewGuid();
}
