using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InventoryWorker.Messaging;

/// <summary>
/// Base class for RabbitMQ-consuming background services with robust handling of connection
/// failures:
///   - The connection is NOT opened eagerly in the constructor or in StartAsync — if it were,
///     a RabbitMQ outage at boot would throw out of IHostedService.StartAsync, which stops
///     the whole host. Instead, <see cref="ExecuteAsync"/> retries the initial connection
///     with backoff (capped at 30s) until it succeeds or the host is stopping, so a broker
///     outage only delays this consumer — it never crashes the app.
///   - Once connected, RabbitMQ.Client's AutomaticRecoveryEnabled handles reconnecting (and
///     redeclaring queues/consumers) after a later, transient connection drop on its own;
///     this base class just logs those transitions for visibility.
/// </summary>
public abstract class RabbitMqConsumerBase : BackgroundService
{
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private readonly RabbitMqOptions _options;
    private readonly ILogger _logger;
    private readonly ConnectionFactory _factory;

    protected IConnection? Connection { get; private set; }

    protected IModel? Channel { get; private set; }

    protected RabbitMqConsumerBase(RabbitMqOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        _factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            UserName = options.UserName,
            Password = options.Password,
            VirtualHost = options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
            DispatchConsumersAsync = true,
        };
    }

    /// <summary>
    /// Declares this consumer's queue(s) and attaches its consumer(s) on the given channel.
    /// Called once after every successful (re)connect.
    /// </summary>
    protected abstract void OnConnected(IModel channel, CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectWithRetryAsync(stoppingToken);

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // RabbitMQ.Client dispatches deliveries on its own threads; this just keeps the
            // hosted service alive until shutdown is requested.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = _factory.CreateConnection();
                connection.ConnectionShutdown += (_, e) =>
                    _logger.LogWarning(
                        "RabbitMQ connection lost ({ReplyText}); the client will retry automatically.",
                        e.ReplyText);

                if (connection is IAutorecoveringConnection recoveringConnection)
                {
                    recoveringConnection.RecoverySucceeded += (_, _) =>
                        _logger.LogInformation("RabbitMQ connection recovered after a transient failure.");
                }

                var channel = connection.CreateModel();
                OnConnected(channel, stoppingToken);

                Connection = connection;
                Channel = channel;

                _logger.LogInformation("Connected to RabbitMQ ({Host}:{Port}).", _options.HostName, _options.Port);
                return;
            }
            catch (Exception ex) when (RabbitMqTransientErrors.IsTransient(ex))
            {
                attempt++;
                var delaySeconds = Math.Min(MaxReconnectDelay.TotalSeconds, 5 * attempt);
                var delay = TimeSpan.FromSeconds(delaySeconds);
                _logger.LogError(
                    ex,
                    "Could not connect to RabbitMQ ({Host}:{Port}), attempt {Attempt}. Retrying in {Delay}s.",
                    _options.HostName, _options.Port, attempt, delay.TotalSeconds);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public override void Dispose()
    {
        Channel?.Dispose();
        Connection?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
