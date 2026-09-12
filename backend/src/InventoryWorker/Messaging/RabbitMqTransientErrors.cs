using System.Net.Sockets;
using RabbitMQ.Client.Exceptions;

namespace InventoryWorker.Messaging;

/// <summary>
/// Classifies exceptions that indicate a RabbitMQ connectivity problem (broker down, network
/// blip, connection/channel closed) as opposed to a programming or data error.
/// </summary>
public static class RabbitMqTransientErrors
{
    public static bool IsTransient(Exception ex) => ex is
        BrokerUnreachableException or
        SocketException or
        AlreadyClosedException or
        TimeoutException or
        OperationInterruptedException;
}
