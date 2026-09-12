using InventoryWorker.Contracts;
using InventoryWorker.Data;
using InventoryWorker.Models;
using InventoryWorker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Tests.InventoryWorker;

public class StockReservationServiceTests
{
    private static (SqliteConnection Connection, InventoryDbContext Db) CreateDb()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        return (connection, db);
    }

    private static OrderCreatedEvent MakeEvent(Guid orderId, string sku, int quantity) => new()
    {
        OrderId = orderId,
        EventId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        Items = new List<OrderCreatedItem> { new() { Sku = sku, Quantity = quantity } },
    };

    [Fact]
    public async Task ReserveAsync_WithSufficientStock_DecrementsStockAndReturnsReserved()
    {
        var (connection, db) = CreateDb();
        await using var _ = connection;
        await using var __ = db;

        db.Products.Add(new Product { Sku = "ABC-01", Name = "Producto ABC-01", StockAvailable = 10 });
        await db.SaveChangesAsync();

        var service = new StockReservationService();
        var orderEvent = MakeEvent(Guid.NewGuid(), "ABC-01", 4);

        var result = await service.ReserveAsync(db, orderEvent, CancellationToken.None);

        Assert.False(result.WasAlreadyProcessed);
        Assert.Equal(StockReservationOutcome.Reserved, result.Reservation.Outcome);
        Assert.Null(result.Reservation.RejectionReason);

        var product = await db.Products.SingleAsync(p => p.Sku == "ABC-01");
        Assert.Equal(6, product.StockAvailable);
    }

    [Fact]
    public async Task ReserveAsync_WithInsufficientStock_RejectsWithoutChangingStock()
    {
        var (connection, db) = CreateDb();
        await using var _ = connection;
        await using var __ = db;

        db.Products.Add(new Product { Sku = "GHI-03", Name = "Producto GHI-03", StockAvailable = 2 });
        await db.SaveChangesAsync();

        var service = new StockReservationService();
        var orderEvent = MakeEvent(Guid.NewGuid(), "GHI-03", 5);

        var result = await service.ReserveAsync(db, orderEvent, CancellationToken.None);

        Assert.False(result.WasAlreadyProcessed);
        Assert.Equal(StockReservationOutcome.Rejected, result.Reservation.Outcome);
        Assert.Contains("Stock insuficiente", result.Reservation.RejectionReason);

        var product = await db.Products.SingleAsync(p => p.Sku == "GHI-03");
        Assert.Equal(2, product.StockAvailable); // unchanged — all-or-nothing
    }

    /// <summary>
    /// The critical idempotency guarantee: reprocessing the same OrderId
    /// (e.g. a RabbitMQ redelivery) must never decrement stock a second time.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_CalledTwiceForSameOrderId_IsIdempotent()
    {
        var (connection, db) = CreateDb();
        await using var _ = connection;
        await using var __ = db;

        db.Products.Add(new Product { Sku = "DEF-02", Name = "Producto DEF-02", StockAvailable = 10 });
        await db.SaveChangesAsync();

        var service = new StockReservationService();
        var orderId = Guid.NewGuid();
        var firstDelivery = MakeEvent(orderId, "DEF-02", 4);
        // Simulates RabbitMQ redelivering the exact same message: same OrderId/EventId.
        var redelivery = new OrderCreatedEvent
        {
            OrderId = orderId,
            EventId = firstDelivery.EventId,
            CorrelationId = firstDelivery.CorrelationId,
            Items = firstDelivery.Items,
        };

        var first = await service.ReserveAsync(db, firstDelivery, CancellationToken.None);
        var second = await service.ReserveAsync(db, redelivery, CancellationToken.None);

        Assert.False(first.WasAlreadyProcessed);
        Assert.True(second.WasAlreadyProcessed);
        Assert.Equal(first.Reservation.Id, second.Reservation.Id);

        var product = await db.Products.SingleAsync(p => p.Sku == "DEF-02");
        Assert.Equal(6, product.StockAvailable); // decremented once (10-4), not twice (would be 2)

        Assert.Single(db.StockReservations); // only one ledger row for this OrderId
    }
}
