namespace OrderFlow.Shared.Messaging;

/// <summary>
/// Broker connection settings. Bound from configuration section "RabbitMq", which in turn
/// is populated from environment variables (RabbitMq__HostName, RabbitMq__Port, etc.) so no
/// credentials ever need to be hardcoded in appsettings.json.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>Queue used to publish/consume order-created events.</summary>
    public string OrderCreatedQueue { get; set; } = "order-created";
}
