using InventoryWorker.Data;
using InventoryWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryWorker.Seed;

/// <summary>
/// Applies pending migrations and loads the initial stock catalog on startup, so the system
/// is usable immediately after `docker compose up` without any manual seeding step.
/// </summary>
public static class InventorySeeder
{
    // Product.Name is user-facing (rendered as-is in the frontend's Spanish-language SKU
    // picker), so it stays in Spanish like the rest of that UI — unlike code comments, log
    // messages and exceptions, which are in English throughout this codebase.
    private static readonly Product[] InitialProducts =
    {
        new() { Sku = "ABC-01", Name = "Producto ABC-01", StockAvailable = 100 },
        new() { Sku = "DEF-02", Name = "Producto DEF-02", StockAvailable = 50 },
        new() { Sku = "GHI-03", Name = "Producto GHI-03", StockAvailable = 75 },
    };

    public static async Task SeedAsync(InventoryDbContext dbContext, ILogger logger, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        foreach (var product in InitialProducts)
        {
            var exists = await dbContext.Products.AnyAsync(p => p.Sku == product.Sku, cancellationToken);
            if (exists)
            {
                continue;
            }

            product.UpdatedAtUtc = DateTime.UtcNow;
            dbContext.Products.Add(product);
            logger.LogInformation("Seeding initial stock for SKU {Sku}: {Stock} units", product.Sku, product.StockAvailable);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
