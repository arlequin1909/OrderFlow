using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OrdersApi.Models;
using OrdersApi.Requests;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

/// <summary>
/// HTTP surface only — validation is delegated to FluentValidation, persistence and
/// orchestration to <see cref="IOrderService"/>. No database access happens here directly.
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IValidator<CreateOrderRequest> _validator;

    public OrdersController(IOrderService orderService, IValidator<CreateOrderRequest> validator)
    {
        _orderService = orderService;
        _validator = validator;
    }

    [HttpPost]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var modelState = new ModelStateDictionary();
            foreach (var error in validationResult.Errors)
            {
                modelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(modelState);
        }

        var order = await _orderService.CreateOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Order>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByIdAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetAll(CancellationToken cancellationToken)
    {
        var orders = await _orderService.GetAllOrdersAsync(cancellationToken);
        return Ok(orders);
    }
}
