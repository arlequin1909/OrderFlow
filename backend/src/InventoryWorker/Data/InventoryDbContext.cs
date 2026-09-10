using InventoryWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryWorker.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("stock");
            entity.HasIndex(p => p.Sku).IsUnique();
            entity.Property(p => p.Sku).IsRequired();
            entity.Property(p => p.Name).IsRequired();
        });
    }
}
