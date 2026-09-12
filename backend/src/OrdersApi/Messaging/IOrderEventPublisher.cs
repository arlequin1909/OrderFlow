using OrdersApi.Contracts;

namespace OrdersApi.Messaging;

public interface IOrderEventPublisher
{
    /// <summary>
    /// Attempts to publish an OrderCreated event. Never throws for broker-availability or
    /// schema-validation problems — returns false with <paramref name="error"/> set instead,
    /// so the caller can keep the already-persisted order and decide how to react.
    /// See README "Broker failure handling" for the full policy.
    /// </summary>
    bool TryPublishOrderCreated(OrderCreatedEvent orderEvent, out string? error);
}
