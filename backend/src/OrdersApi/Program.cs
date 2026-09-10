using Microsoft.EntityFrameworkCore;
using OrderFlow.Shared.Messaging;
using OrdersApi.Data;
using OrdersApi.Messaging;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from configuration key "ConnectionStrings:Postgres", which is
// overridable via the environment variable ConnectionStrings__Postgres (see docker-compose.yml).
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Postgres'.");

builder.Services.AddDbContext<OrdersDbContext>(options => options.UseNpgsql(connectionString));

// RabbitMq:* is populated from RabbitMq__HostName, RabbitMq__UserName, RabbitMq__Password, etc.
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<IOrderEventPublisher, RabbitMqPublisher>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply pending migrations for the Orders schema on startup.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
