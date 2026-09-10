using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrdersApi.Controllers;
using OrdersApi.Data;

namespace OrderFlow.Tests.TestSupport;

/// <summary>
/// Builds an <see cref="OrdersController"/> wired to real EF Core DbContexts backed by
/// SQLite in-memory databases (not mocked DbContexts) — real query/constraint behavior,
/// no Postgres required — plus a <see cref="FakeOrderEventPublisher"/> instead of RabbitMQ.
/// </summary>
public sealed class OrdersControllerHarness : IAsyncDisposable
{
    private readonly SqliteConnection _ordersConnection;
    private readonly SqliteConnection _catalogConnection;

    public OrdersDbContext OrdersDb { get; }

    public CatalogDbContext CatalogDb { get; }

    public FakeOrderEventPublisher Publisher { get; } = new();

    public OrdersController Controller { get; }

    public OrdersControllerHarness()
    {
        _ordersConnection = new SqliteConnection("DataSource=:memory:");
        _ordersConnection.Open();
        OrdersDb = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlite(_ordersConnection).Options);
        OrdersDb.Database.EnsureCreated();

        _catalogConnection = new SqliteConnection("DataSource=:memory:");
        _catalogConnection.Open();
        CatalogDb = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConnection).Options);
        CatalogDb.Database.EnsureCreated();

        Controller = new OrdersController(OrdersDb, CatalogDb, Publisher, NullLogger<OrdersController>.Instance);
    }

    /// <summary>Seeds a SKU into the (otherwise InventoryWorker-owned) catalog so it passes existence validation.</summary>
    public void SeedCatalogSku(string sku)
    {
        CatalogDb.Products.Add(new CatalogProduct { Sku = sku });
        CatalogDb.SaveChanges();
    }

    public async ValueTask DisposeAsync()
    {
        await OrdersDb.DisposeAsync();
        await CatalogDb.DisposeAsync();
        await _ordersConnection.DisposeAsync();
        await _catalogConnection.DisposeAsync();
    }
}
