using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CaroNet.Client.WinUI.Services;
using CaroNet.Shared.Protocol;

namespace CaroNet.Client.WinUI.Tests;

public sealed class SocketClientConnectionTests
{
    [Fact]
    public async Task SendAsync_writes_length_prefixed_protocol_frame()
    {
        await using var server = new LoopbackServer();
        await using var client = new SocketClientConnection();

        Task<Socket> acceptedClient = server.AcceptSocketAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), server.Port, CancellationToken.None);
        using Socket serverSocket = await acceptedClient;

        await client.SendAsync(
            new MessageEnvelope { Type = MessageType.Hello },
            CancellationToken.None);

        byte[] frame = await ReadFrameAsync(serverSocket, CancellationToken.None);
        MessageEnvelope envelope = ProtocolFrameCodec.Decode(frame);

        Assert.Equal(MessageType.Hello, envelope.Type);
    }

    [Fact]
    public async Task ReceiveLoop_reads_frame_when_tcp_splits_length_and_payload()
    {
        await using var server = new LoopbackServer();
        await using var client = new SocketClientConnection();
        var received = new TaskCompletionSource<MessageEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.MessageReceived += (_, args) => received.TrySetResult(args.Message);

        Task<Socket> acceptedClient = server.AcceptSocketAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), server.Port, CancellationToken.None);
        using Socket serverSocket = await acceptedClient;

        byte[] frame = ProtocolFrameCodec.Encode(
            new MessageEnvelope { Type = MessageType.Hello });

        await serverSocket.SendAsync(frame.AsMemory(0, 2), SocketFlags.None);
        await Task.Delay(25);
        await serverSocket.SendAsync(frame.AsMemory(2), SocketFlags.None);

        MessageEnvelope envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(MessageType.Hello, envelope.Type);
    }

    [Fact]
    public async Task ReceiveLoop_reports_protocol_error_for_malformed_frame()
    {
        await using var server = new LoopbackServer();
        await using var client = new SocketClientConnection();
        var error = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.ConnectionError += (_, ex) => error.TrySetResult(ex);

        Task<Socket> acceptedClient = server.AcceptSocketAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), server.Port, CancellationToken.None);
        using Socket serverSocket = await acceptedClient;

        byte[] invalidPayload = "{}"u8.ToArray();
        byte[] frame = new byte[4 + invalidPayload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), invalidPayload.Length);
        invalidPayload.CopyTo(frame.AsSpan(4));

        await serverSocket.SendAsync(frame, SocketFlags.None);

        Exception exception = await error.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task ReceiveLoop_Throws_IfPayloadTooLarge()
    {
        await using var server = new LoopbackServer();
        await using var client = new SocketClientConnection();
        var error = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        client.ConnectionError += (_, ex) => error.TrySetResult(ex);

        Task<Socket> acceptedClient = server.AcceptSocketAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), server.Port, CancellationToken.None);
        using Socket serverSocket = await acceptedClient;

        // Gửi header chỉ độ dài 2 MB (lớn hơn MaxPayloadLength = 1 MB) (Issue #61)
        byte[] frame = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), 2 * 1024 * 1024);

        await serverSocket.SendAsync(frame, SocketFlags.None);

        Exception exception = await error.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("vượt quá giới hạn tối đa", exception.Message);
    }

    [Fact]
    public async Task Concurrent_SendAsync_calls_do_not_interleave_frames()
    {
        await using var server = new LoopbackServer();
        await using var client = new SocketClientConnection();
        Task<Socket> acceptedClient = server.AcceptSocketAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), server.Port, CancellationToken.None);
        using Socket serverSocket = await acceptedClient;

        MessageType[] messageTypes =
        [
            MessageType.Hello,
            MessageType.CreateRoom,
            MessageType.JoinRoom,
            MessageType.MakeMove,
            MessageType.Ready
        ];

        await Task.WhenAll(
            messageTypes.Select(type =>
                client.SendAsync(
                    new MessageEnvelope { Type = type },
                    CancellationToken.None)));

        var decodedTypes = new ConcurrentBag<MessageType>();
        for (var index = 0; index < messageTypes.Length; index++)
        {
            byte[] frame = await ReadFrameAsync(serverSocket, CancellationToken.None);
            decodedTypes.Add(ProtocolFrameCodec.Decode(frame).Type);
        }

        Assert.Empty(messageTypes.Except(decodedTypes));
    }

    [Fact]
    public async Task DisconnectAsync_raises_disconnected_event_once()
    {
        await using var server = new LoopbackServer();
        await using var client = new SocketClientConnection();
        var disconnectedCount = 0;

        client.Disconnected += (_, _) => Interlocked.Increment(ref disconnectedCount);

        Task<Socket> acceptedClient = server.AcceptSocketAsync();
        await client.ConnectAsync(IPAddress.Loopback.ToString(), server.Port, CancellationToken.None);
        using Socket serverSocket = await acceptedClient;

        await client.DisconnectAsync();
        await client.DisconnectAsync();

        Assert.Equal(1, disconnectedCount);
    }

    private static async Task<byte[]> ReadFrameAsync(
        Socket socket,
        CancellationToken cancellationToken)
    {
        byte[] lengthPrefix = await ReadExactlyAsync(socket, 4, cancellationToken);
        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
        byte[] payload = await ReadExactlyAsync(socket, payloadLength, cancellationToken);

        byte[] frame = new byte[4 + payloadLength];
        lengthPrefix.CopyTo(frame.AsSpan(0, 4));
        payload.CopyTo(frame.AsSpan(4));
        return frame;
    }

    private static async Task<byte[]> ReadExactlyAsync(
        Socket socket,
        int length,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            int read = await socket.ReceiveAsync(
                buffer.AsMemory(offset, length - offset),
                SocketFlags.None,
                cancellationToken);

            if (read == 0)
            {
                throw new IOException("Socket closed before enough bytes were received.");
            }

            offset += read;
        }

        return buffer;
    }

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly Socket _listener = new(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        public LoopbackServer()
        {
            _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _listener.Listen();
            Port = ((IPEndPoint)_listener.LocalEndPoint!).Port;
        }

        public int Port { get; }

        public Task<Socket> AcceptSocketAsync()
        {
            return _listener.AcceptAsync();
        }

        public ValueTask DisposeAsync()
        {
            _listener.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
