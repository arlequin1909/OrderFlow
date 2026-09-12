namespace InventoryWorker.Contracts;

public sealed class OrderCreatedItem
{
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
