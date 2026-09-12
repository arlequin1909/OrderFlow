using OrdersApi.Models;
using OrdersApi.Requests;

namespace OrdersApi.Services;

/// <summary>All database operations and orchestration for orders — kept out of the controller for a strict Single Responsibility split.</summary>
public interface IOrderService
{
    /// <summary>
    /// Persists a new order as <see cref="OrderStatus.Pending"/> and publishes the
    /// OrderCreated event. Assumes <paramref name="request"/> already passed validation.
    /// </summary>
    Task<Order> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);

    Task<Order?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> GetAllOrdersAsync(CancellationToken cancellationToken);

    /// <summary>Moves an order from Pending to Confirmed (no-op if it isn't still Pending).</summary>
    Task ConfirmOrderAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Moves an order from Pending to Rejected (no-op if it isn't still Pending).</summary>
    Task RejectOrderAsync(Guid orderId, string reason, CancellationToken cancellationToken);
}
