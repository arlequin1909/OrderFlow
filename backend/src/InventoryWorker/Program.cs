using InventoryWorker.Data;
using InventoryWorker.Messaging;
using InventoryWorker.Seed;
using InventoryWorker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OrderFlow.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from configuration key "ConnectionStrings:Postgres", which is
// overridable via the environment variable ConnectionStrings__Postgres (see docker-compose.yml).
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Postgres'.");

// See OrdersApi/Program.cs for why each service uses its own migrations history table name
// despite sharing one physical Postgres instance in this docker-compose setup.
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_inventory")));

// RabbitMq:* is populated from RabbitMq__HostName, RabbitMq__UserName, RabbitMq__Password, etc.
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddSingleton<IStockOutcomePublisher, InventoryEventPublisher>();
builder.Services.AddScoped<StockReservationService>();
builder.Services.AddHostedService<OrderCreatedConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OrderFlow — InventoryWorker",
        Version = "v1",
        Description = "Catálogo de stock. Consume order-created de RabbitMQ de forma " +
            "idempotente y publica stock-reserved/stock-rejected con el resultado.",
    });
});

// Allowed origins for the frontend/ SPA — comma-separated, overridable via the environment
// variable Cors__AllowedOrigins. Defaults to the Vite dev server's default port.
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// Auto-seed: apply migrations and load the initial stock catalog before accepting traffic.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await InventorySeeder.SeedAsync(dbContext, logger);
}

// Swagger UI is intentionally enabled in every environment (including the Docker/Production
// deployment via docker-compose) so an evaluator can exercise the API directly from
// http://localhost:5080/swagger without switching ASPNETCORE_ENVIRONMENT. For a real
// production deployment this would normally be gated behind Development/staging only.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "InventoryWorker v1");
});

app.UseCors("Frontend");
app.UseAuthorization();
app.MapControllers();

app.Run();
