namespace OrdersApi.Messaging;

/// <summary>
/// Broker connection settings. Bound from configuration section "RabbitMq", populated from
/// environment variables (RabbitMq__HostName, RabbitMq__Port, etc.) so no credentials ever
/// need to be hardcoded.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>Queue used to publish order-created events.</summary>
    public string OrderCreatedQueue { get; set; } = "order-created";

    /// <summary>Queue consumed when InventoryWorker reserved stock successfully.</summary>
    public string StockReservedQueue { get; set; } = "stock-reserved";

    /// <summary>Queue consumed when InventoryWorker could not reserve stock.</summary>
    public string StockRejectedQueue { get; set; } = "stock-rejected";
}
