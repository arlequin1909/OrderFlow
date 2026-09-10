using InventoryWorker.Data;
using InventoryWorker.Messaging;
using InventoryWorker.Seed;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Shared.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from configuration key "ConnectionStrings:Postgres", which is
// overridable via the environment variable ConnectionStrings__Postgres (see docker-compose.yml).
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Postgres'.");

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connectionString));

// RabbitMq:* is populated from RabbitMq__HostName, RabbitMq__UserName, RabbitMq__Password, etc.
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddHostedService<OrderCreatedConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Auto-seed: apply migrations and load the initial stock catalog before accepting traffic.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await InventorySeeder.SeedAsync(dbContext, logger);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
