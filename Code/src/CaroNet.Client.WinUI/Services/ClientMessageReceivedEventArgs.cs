using System;
using CaroNet.Shared.Protocol;

namespace CaroNet.Client.WinUI.Services;

public sealed class ClientMessageReceivedEventArgs : EventArgs
{
    public ClientMessageReceivedEventArgs(MessageEnvelope message)
    {
        Message = message;
    }

    public MessageEnvelope Message { get; }
}
