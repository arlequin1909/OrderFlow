using Microsoft.EntityFrameworkCore;

namespace OrdersApi.Data;

/// <summary>
/// Read-only projection of the "stock" table owned by InventoryWorker, used exclusively to
/// validate that a SKU exists in the catalog when creating an order.
///
/// Trade-off: this reads InventoryWorker's table directly instead of calling its HTTP API
/// (GET /api/stock/{sku}), because both services already share the same Postgres instance in
/// this docker-compose setup and a synchronous cross-service HTTP call would add another
/// runtime dependency (and failure mode) to order creation for little benefit at this stage.
/// In a stricter microservices setup with separate databases per service, this should become
/// an HTTP call to InventoryWorker (with its own timeout/circuit-breaker handling).
///
/// This context never runs migrations and never writes — InventoryWorker remains the sole
/// owner of the table's schema and data.
/// </summary>
public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options)
    {
    }

    public DbSet<CatalogProduct> Products => Set<CatalogProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogProduct>(entity =>
        {
            entity.ToTable("stock");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Sku).IsRequired();
        });
    }
}

/// <summary>Minimal read-only view of InventoryWorker's Product/stock row.</summary>
public class CatalogProduct
{
    public int Id { get; set; }

    public string Sku { get; set; } = string.Empty;
}
