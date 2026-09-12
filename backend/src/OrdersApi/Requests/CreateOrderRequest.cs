namespace OrdersApi.Requests;

public record CreateOrderRequest(string ClienteNombre, List<CreateOrderItemRequest> Items);
