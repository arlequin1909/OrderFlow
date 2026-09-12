namespace OrdersApi.Models;

public class OrderItem
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
