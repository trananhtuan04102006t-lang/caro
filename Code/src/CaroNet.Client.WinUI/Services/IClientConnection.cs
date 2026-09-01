using System;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Shared.Protocol;

namespace CaroNet.Client.WinUI.Services;

public interface IClientConnection : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken);

    Task DisconnectAsync();

    Task SendAsync(MessageEnvelope message, CancellationToken cancellationToken);

    event EventHandler<ClientMessageReceivedEventArgs>? MessageReceived;

    event EventHandler<Exception>? ConnectionError;

    event EventHandler? Disconnected;
}
