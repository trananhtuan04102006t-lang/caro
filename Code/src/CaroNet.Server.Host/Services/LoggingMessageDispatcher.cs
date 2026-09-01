using CaroNet.Server.Host.Networking;
using CaroNet.Shared.Protocol;

namespace CaroNet.Server.Host.Services;

public sealed class LoggingMessageDispatcher
    : IMessageDispatcher
{
    public Task DispatchAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[MESSAGE] Client={session.Id} Type={message.Type}");

        return Task.CompletedTask;
    }
}