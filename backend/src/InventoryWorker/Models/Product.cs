using System.ComponentModel.DataAnnotations;

namespace InventoryWorker.Models;

/// <summary>Stock entry for a single SKU.</summary>
public class Product
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string Sku { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int StockAvailable { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
