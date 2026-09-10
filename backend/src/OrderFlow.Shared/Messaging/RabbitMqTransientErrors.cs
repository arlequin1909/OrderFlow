using System.Net.Sockets;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Shared.Messaging;

/// <summary>
/// Classifies exceptions that indicate a RabbitMQ connectivity problem (broker down,
/// network blip, connection/channel closed) as opposed to a programming or data error.
/// Shared by every publisher/consumer so "is this broker down?" is answered consistently.
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
