namespace OrdersApi.Models;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }

    public Guid OrderId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
