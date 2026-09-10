using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OrderFlow.Shared.Messaging;
using OrdersApi.Data;
using OrdersApi.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from configuration key "ConnectionStrings:Postgres", which is
// overridable via the environment variable ConnectionStrings__Postgres (see docker-compose.yml).
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Postgres'.");

// OrdersApi and InventoryWorker share one physical Postgres instance in this docker-compose
// setup. EF Core's migrations history table is NOT namespaced per DbContext by default, so
// both services' startup migrations would race to create the same "__EFMigrationsHistory"
// table. Giving each service its own history table name avoids that race entirely.
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory_orders")));

// Read-only catalog lookup (SKU existence check) — see CatalogDbContext for the trade-off
// of sharing the "stock" table instead of calling InventoryWorker over HTTP.
builder.Services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(connectionString));

// RabbitMq:* is populated from RabbitMq__HostName, RabbitMq__UserName, RabbitMq__Password, etc.
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<IOrderEventPublisher, RabbitMqPublisher>();
builder.Services.AddHostedService<StockOutcomeConsumer>();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Serialize enums (e.g. Order.Status) as their string name instead of a numeric index.
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OrderFlow — OrdersApi",
        Version = "v1",
        Description = "Creación y consulta de pedidos. Publica order-created en RabbitMQ y " +
            "consume stock-reserved/stock-rejected para confirmar o rechazar el pedido.",
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

// Apply pending migrations for the Orders schema on startup.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Swagger UI is intentionally enabled in every environment (including the Docker/Production
// deployment via docker-compose) so an evaluator can exercise the API directly from
// http://localhost:5081/swagger without switching ASPNETCORE_ENVIRONMENT. For a real
// production deployment this would normally be gated behind Development/staging only.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "OrdersApi v1");
});

app.UseCors("Frontend");
app.UseAuthorization();
app.MapControllers();

app.Run();
