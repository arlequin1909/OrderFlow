using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Shared.Contracts;
using OrdersApi.Data;
using OrdersApi.Messaging;
using OrdersApi.Models;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrdersDbContext _dbContext;
    private readonly IOrderEventPublisher _publisher;

    public OrdersController(OrdersDbContext dbContext, IOrderEventPublisher publisher)
    {
        _dbContext = dbContext;
        _publisher = publisher;
    }

    public record CreateOrderItemRequest(string Sku, int Quantity);
    public record CreateOrderRequest(List<CreateOrderItemRequest> Items);

    [HttpPost]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest("La orden debe contener al menos un item.");
        }

        var order = new Order
        {
            Items = request.Items.Select(i => new OrderItem { Sku = i.Sku, Quantity = i.Quantity }).ToList(),
        };

        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var orderEvent = new OrderCreatedEvent
        {
            OrderId = order.Id,
            Items = order.Items.Select(i => new OrderCreatedItem { Sku = i.Sku, Quantity = i.Quantity }).ToList(),
        };
        _publisher.PublishOrderCreated(orderEvent);

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Order>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.Include(o => o.Items).AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetAll(CancellationToken cancellationToken)
    {
        var orders = await _dbContext.Orders.Include(o => o.Items).AsNoTracking()
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return Ok(orders);
    }
}
