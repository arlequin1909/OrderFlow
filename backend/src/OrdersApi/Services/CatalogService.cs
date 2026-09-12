using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;
using OrdersApi.Models;

namespace OrdersApi.Services;

public class CatalogService : ICatalogService
{
    private readonly CatalogDbContext _dbContext;

    public CatalogService(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken) =>
        _dbContext.Products.AsNoTracking().AnyAsync(p => p.Sku == sku, cancellationToken);

    public async Task<IReadOnlyList<CatalogProduct>> GetAllAsync(CancellationToken cancellationToken) =>
        await _dbContext.Products.AsNoTracking().OrderBy(p => p.Sku).ToListAsync(cancellationToken);
}
