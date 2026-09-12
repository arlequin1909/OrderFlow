using System.Text;
using System.Text.Json;
using OrdersApi.Contracts;
using OrdersApi.Services;

namespace OrdersApi.Messaging;

/// <summary>Deserializes a stock outcome message and delegates the order status transition to <see cref="IOrderService"/>.</summary>
public class StockOutcomeEventHandler : IStockOutcomeEventHandler
{
    private readonly IOrderService _orderService;

    public StockOutcomeEventHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task HandleStockReservedAsync(byte[] messageBody, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(messageBody);
        var stockEvent = JsonSerializer.Deserialize<StockReservedEvent>(json)
            ?? throw new JsonException("Null or malformed payload.");

        await _orderService.ConfirmOrderAsync(stockEvent.OrderId, cancellationToken);
    }

    public async Task HandleStockRejectedAsync(byte[] messageBody, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(messageBody);
        var stockEvent = JsonSerializer.Deserialize<StockRejectedEvent>(json)
            ?? throw new JsonException("Null or malformed payload.");

        await _orderService.RejectOrderAsync(stockEvent.OrderId, stockEvent.Reason, cancellationToken);
    }
}
