using OrdersApi.Models;

namespace OrdersApi.Services;

/// <summary>Read-only access to InventoryWorker's catalog, for SKU validation and the frontend's product picker.</summary>
public interface ICatalogService
{
    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogProduct>> GetAllAsync(CancellationToken cancellationToken);
}
