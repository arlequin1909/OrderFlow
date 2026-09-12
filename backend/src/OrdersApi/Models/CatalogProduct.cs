namespace OrdersApi.Models;

/// <summary>
/// Read-only projection of InventoryWorker's Product/stock row — the fields OrdersApi needs
/// to validate a SKU and to serve the read-only GET /api/catalog endpoint for the frontend.
/// </summary>
public class CatalogProduct
{
    public int Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int StockAvailable { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
