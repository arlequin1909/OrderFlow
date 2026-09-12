namespace OrdersApi.Requests;

public record CreateOrderItemRequest(string Sku, int Quantity);
