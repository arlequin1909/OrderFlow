using OrderFlow.Shared.Contracts;
using OrdersApi.Messaging;

namespace OrderFlow.Tests.TestSupport;

/// <summary>In-memory stand-in for RabbitMQ so controller tests never touch a real broker.</summary>
public sealed class FakeOrderEventPublisher : IOrderEventPublisher
{
    public List<OrderCreatedEvent> Published { get; } = new();

    public bool ShouldSucceed { get; set; } = true;

    public bool TryPublishOrderCreated(OrderCreatedEvent orderEvent, out string? error)
    {
        Published.Add(orderEvent);
        error = ShouldSucceed ? null : "simulated broker failure";
        return ShouldSucceed;
    }
}
