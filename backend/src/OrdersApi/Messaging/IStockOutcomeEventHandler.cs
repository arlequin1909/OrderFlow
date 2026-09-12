namespace OrdersApi.Messaging;

/// <summary>
/// Business logic for reacting to InventoryWorker's stock outcome events, decoupled from the
/// RabbitMQ transport (<see cref="StockOutcomeConsumer"/>) so it depends only on the raw
/// message payload, not on RabbitMQ types.
/// </summary>
public interface IStockOutcomeEventHandler
{
    Task HandleStockReservedAsync(byte[] messageBody, CancellationToken cancellationToken);

    Task HandleStockRejectedAsync(byte[] messageBody, CancellationToken cancellationToken);
}
