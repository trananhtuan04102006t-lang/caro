using CaroNet.Server.Host.Networking;
using CaroNet.Shared.Protocol;

namespace CaroNet.Server.Host.Services;

public interface IMessageDispatcher
{
    Task DispatchAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken);
}