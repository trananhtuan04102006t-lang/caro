using System.Net.Sockets;
using System.Threading;
using CaroNet.Server.Host.Services;
using CaroNet.Shared.Protocol;

namespace CaroNet.Server.Host.Networking;

public sealed class ClientSession
{
    private readonly IMessageDispatcher _dispatcher;

    private readonly SemaphoreSlim _sendLock =
        new(1, 1);

    public Guid Id { get; } =
        Guid.NewGuid();

    public Socket Socket { get; }

    public ClientSession(
        Socket socket,
        IMessageDispatcher dispatcher)
    {
        Socket = socket;
        _dispatcher = dispatcher;
    }

    public async Task SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        byte[] frame =
            ProtocolFrameCodec.Encode(
                message);

        await _sendLock.WaitAsync(
            cancellationToken);

        try
        {
            int totalSent = 0;

            while (totalSent < frame.Length)
            {
                int sent =
                    await Socket.SendAsync(
                        frame.AsMemory(totalSent),
                        SocketFlags.None,
                        cancellationToken);

                if (sent == 0)
                {
                    throw new InvalidOperationException(
                        "Socket closed while sending.");
                }

                totalSent += sent;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task RunAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[CONNECT] {Id}");

        try
        {
            byte[] buffer =
                new byte[4096];

            var frameReader =
                new ProtocolFrameReader();

            while (!cancellationToken.IsCancellationRequested)
            {
                int received =
                    await Socket.ReceiveAsync(
                        buffer,
                        SocketFlags.None,
                        cancellationToken);

                if (received == 0)
                {
                    break;
                }

                frameReader.Append(
                    buffer.AsSpan(0, received));

                while (
                    frameReader.TryReadFrame(
                        out byte[] frame))
                {
                    try
                    {
                        MessageEnvelope message =
                            ProtocolFrameCodec.Decode(
                                frame);

                        await _dispatcher.DispatchAsync(
                            this,
                            message,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"[PROTOCOL ERROR] {Id}: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ERROR] {Id}: {ex.Message}");
        }
        finally
        {
            Console.WriteLine(
                $"[DISCONNECT] {Id}");

            Socket.Dispose();
        }
    }
}