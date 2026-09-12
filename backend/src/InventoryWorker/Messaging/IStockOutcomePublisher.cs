using InventoryWorker.Contracts;

namespace InventoryWorker.Messaging;

public interface IStockOutcomePublisher
{
    /// <summary>Never throws for broker-availability problems — returns false with <paramref name="error"/> set instead.</summary>
    bool TryPublishStockReserved(StockReservedEvent stockEvent, out string? error);

    /// <summary>Never throws for broker-availability problems — returns false with <paramref name="error"/> set instead.</summary>
    bool TryPublishStockRejected(StockRejectedEvent stockEvent, out string? error);
}
