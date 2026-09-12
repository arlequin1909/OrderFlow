using Microsoft.EntityFrameworkCore;
using OrdersApi.Contracts;
using OrdersApi.Data;
using OrdersApi.Messaging;
using OrdersApi.Models;
using OrdersApi.Requests;

namespace OrdersApi.Services;

public class OrderService : IOrderService
{
    private readonly OrdersDbContext _dbContext;
    private readonly IOrderEventPublisher _publisher;
    private readonly ILogger<OrderService> _logger;

    public OrderService(OrdersDbContext dbContext, IOrderEventPublisher publisher, ILogger<OrderService> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = new Order
        {
            ClienteNombre = request.ClienteNombre.Trim(),
            Status = OrderStatus.Pending,
            Items = request.Items.Select(i => new OrderItem { Sku = i.Sku, Quantity = i.Quantity }).ToList(),
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var orderEvent = new OrderCreatedEvent
        {
            OrderId = order.Id,
            Items = order.Items.Select(i => new OrderCreatedItem { Sku = i.Sku, Quantity = i.Quantity }).ToList(),
        };

        // Broker failure handling: TryPublishOrderCreated never throws for connectivity or
        // schema problems, it only returns false. The order persisted above is NOT rolled
        // back or blocked by this — it stays Pending with EventPublished=false so a
        // reconciliation process can retry later. Full rationale in README "Broker failure
        // handling".
        var published = _publisher.TryPublishOrderCreated(orderEvent, out var publishError);
        order.EventPublished = published;
        order.EventPublishError = published ? null : publishError;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!published)
        {
            _logger.LogWarning(
                "Order {OrderId} created as Pending, but the order-created event could not be published: {Error}",
                order.Id, publishError);
        }

        return order;
    }

    public async Task<Order?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Orders.Include(o => o.Items).AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> GetAllOrdersAsync(CancellationToken cancellationToken) =>
        await _dbContext.Orders.Include(o => o.Items).AsNoTracking()
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task ConfirmOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        TransitionStatusAsync(orderId, OrderStatus.Confirmed, rejectionReason: null, cancellationToken);

    public Task RejectOrderAsync(Guid orderId, string reason, CancellationToken cancellationToken) =>
        TransitionStatusAsync(orderId, OrderStatus.Rejected, reason, cancellationToken);

    private async Task TransitionStatusAsync(Guid orderId, OrderStatus newStatus, string? rejectionReason, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            _logger.LogWarning("Received a stock outcome event for order {OrderId}, which does not exist. Discarding.", orderId);
            return;
        }

        if (order.Status != OrderStatus.Pending)
        {
            // Idempotency: a redelivery or a late/out-of-order event must not overwrite an
            // already-settled status.
            _logger.LogInformation(
                "Order {OrderId} was already {Status}; ignoring the {NewStatus} event received.",
                orderId, order.Status, newStatus);
            return;
        }

        order.Status = newStatus;
        order.RejectionReason = rejectionReason;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} updated to {Status}.", orderId, newStatus);
    }
}
