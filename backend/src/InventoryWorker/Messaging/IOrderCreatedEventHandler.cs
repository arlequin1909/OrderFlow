namespace InventoryWorker.Messaging;

/// <summary>
/// Business logic for reacting to an OrderCreated message, decoupled from the RabbitMQ
/// transport (<see cref="OrderCreatedConsumer"/>) so it depends only on the raw message
/// payload, not on RabbitMQ types.
/// </summary>
public interface IOrderCreatedEventHandler
{
    Task HandleAsync(byte[] messageBody, CancellationToken cancellationToken);
}
