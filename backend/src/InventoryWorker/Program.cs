using InventoryWorker.Data;
using InventoryWorker.Messaging;
using InventoryWorker.Seed;
using InventoryWorker.Services;
using Microsoft.EntityFrameworkCore;

// Generic host, not WebApplication — InventoryWorker is a pure background service with no
// HTTP surface at all (no Kestrel, no controllers, no Swagger). Note this means environment
// selection uses the DOTNET_ENVIRONMENT variable, not ASPNETCORE_ENVIRONMENT (that one is
// ASP.NET Core-hosting-specific and has no effect here) — see docker-compose.yml and README
// "Architecture decisions".
var builder = Host.CreateApplicationBuilder(args);

// Connection string comes from configuration key "ConnectionStrings:Postgres", which is
// overridable via the environment variable ConnectionStrings__Postgres (see docker-compose.yml).
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:Postgres'.");

// See OrdersApi/Program.cs for why each service uses its own migrations history table name
// despite sharing one physical Postgres instance in this docker-compose setup.
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_inventory")));

// RabbitMq:* is populated from RabbitMq__HostName, RabbitMq__UserName, RabbitMq__Password, etc.
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddSingleton<IStockOutcomePublisher, InventoryEventPublisher>();
builder.Services.AddScoped<IStockReservationService, StockReservationService>();
builder.Services.AddScoped<IOrderCreatedEventHandler, OrderCreatedEventHandler>();
builder.Services.AddHostedService<OrderCreatedConsumer>();

var host = builder.Build();

// Auto-seed: apply migrations and load the initial stock catalog before consuming any
// messages, so the system is usable immediately after `docker compose up`.
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await InventorySeeder.SeedAsync(dbContext, logger);
}

host.Run();
