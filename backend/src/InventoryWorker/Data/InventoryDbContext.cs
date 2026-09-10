using InventoryWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryWorker.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("stock");
            entity.HasIndex(p => p.Sku).IsUnique();
            entity.Property(p => p.Sku).IsRequired();
            entity.Property(p => p.Name).IsRequired();
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("stock_reservations");
            // Idempotency guard at the database level: even if two deliveries of the same
            // OrderCreated event race past the in-memory check, only one insert succeeds.
            entity.HasIndex(r => r.OrderId).IsUnique();
            entity.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        });
    }
}
