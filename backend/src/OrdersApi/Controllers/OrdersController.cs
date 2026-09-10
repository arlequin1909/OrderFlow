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
    private readonly CatalogDbContext _catalogDbContext;
    private readonly IOrderEventPublisher _publisher;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        OrdersDbContext dbContext,
        CatalogDbContext catalogDbContext,
        IOrderEventPublisher publisher,
        ILogger<OrdersController> logger)
    {
        _dbContext = dbContext;
        _catalogDbContext = catalogDbContext;
        _publisher = publisher;
        _logger = logger;
    }

    public record CreateOrderItemRequest(string Sku, int Quantity);
    public record CreateOrderRequest(string ClienteNombre, List<CreateOrderItemRequest> Items);

    private const int MinQuantity = 1;
    private const int MaxQuantity = 100;

    [HttpPost]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(request, cancellationToken);
        if (errors.Count > 0)
        {
            return BadRequest(new { errors });
        }

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

        // Manejo de fallos del broker: TryPublishOrderCreated nunca lanza para problemas de
        // conectividad o esquema, solo devuelve false. La orden ya persistida arriba NO se
        // revierte ni bloquea la respuesta al cliente — queda en estado Pending con
        // EventPublished=false para que un proceso de reconciliación la reintente más tarde.
        // Detalle completo en README, sección "Manejo de fallos del broker".
        var published = _publisher.TryPublishOrderCreated(orderEvent, out var publishError);
        order.EventPublished = published;
        order.EventPublishError = published ? null : publishError;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!published)
        {
            _logger.LogWarning(
                "Orden {OrderId} creada como Pending, pero el evento order-created no pudo publicarse: {Error}",
                order.Id, publishError);
        }

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

    private async Task<List<string>> ValidateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.ClienteNombre))
        {
            errors.Add("clienteNombre es requerido.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            errors.Add("La orden debe contener al menos un item.");
            return errors;
        }

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
            {
                errors.Add("Cada item debe tener un sku.");
                continue;
            }

            if (item.Quantity < MinQuantity || item.Quantity > MaxQuantity)
            {
                errors.Add($"La cantidad para el SKU '{item.Sku}' debe estar entre {MinQuantity} y {MaxQuantity}.");
            }
        }

        var requestedSkus = request.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Sku))
            .Select(i => i.Sku)
            .Distinct()
            .ToList();

        if (requestedSkus.Count > 0)
        {
            var existingSkus = await _catalogDbContext.Products.AsNoTracking()
                .Where(p => requestedSkus.Contains(p.Sku))
                .Select(p => p.Sku)
                .ToListAsync(cancellationToken);

            foreach (var missingSku in requestedSkus.Except(existingSkus))
            {
                errors.Add($"El SKU '{missingSku}' no existe en el catálogo.");
            }
        }

        return errors;
    }
}
