using Microsoft.EntityFrameworkCore;
using OrdersApi.Models;

namespace OrdersApi.Data;

/// <summary>
/// Read-only projection of the "stock" table owned by InventoryWorker, used for SKU
/// existence validation and for the read-only GET /api/catalog endpoint the frontend uses to
/// populate its SKU picker.
///
/// Trade-off: this reads InventoryWorker's table directly instead of calling an HTTP API,
/// because both services already share the same Postgres instance in this docker-compose
/// setup, and InventoryWorker itself exposes no HTTP surface at all (it is a pure background
/// service — see README "Architecture decisions"). In a stricter microservices setup with
/// separate databases per service, this would need its own replicated read model instead.
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
