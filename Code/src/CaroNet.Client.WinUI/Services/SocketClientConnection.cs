using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Shared.Protocol;

namespace CaroNet.Client.WinUI.Services;

public sealed class SocketClientConnection : IClientConnection
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _disconnectLock = new();
    private Socket? _socket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;
    private bool _isDisconnected = true;

    public bool IsConnected => _socket?.Connected == true && !_isDisconnected;

    public event EventHandler<ClientMessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<Exception>? ConnectionError;

    public event EventHandler? Disconnected;

    public async Task ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        Socket socket = new(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp);

        try
        {
            await socket.ConnectAsync(
                new DnsEndPoint(host, port),
                cancellationToken);

            _socket = socket;
            _receiveLoopCts = new CancellationTokenSource();
            _isDisconnected = false;
            _receiveLoopTask = Task.Run(
                () => ReceiveLoopAsync(_receiveLoopCts.Token),
                CancellationToken.None);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        Socket socket = _socket
            ?? throw new InvalidOperationException("Chưa kết nối server.");

        if (_isDisconnected)
        {
            throw new InvalidOperationException("Kết nối server đã đóng.");
        }

        byte[] frame = ProtocolFrameCodec.Encode(message);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await SendAllAsync(
                socket,
                frame,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (!TryBeginDisconnect())
        {
            return;
        }

        _receiveLoopCts?.Cancel();

        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // Socket có thể đã đóng từ phía server, chỉ cần tiếp tục dọn tài nguyên.
        }
        catch (ObjectDisposedException)
        {
            // Socket đã được dispose bởi nhánh lỗi khác.
        }

        if (_receiveLoopTask is not null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        CleanupConnection();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] frame = await ReadFrameAsync(cancellationToken);
                MessageEnvelope message = ProtocolFrameCodec.Decode(frame);

                MessageReceived?.Invoke(
                    this,
                    new ClientMessageReceivedEventArgs(message));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ConnectionError?.Invoke(this, ex);
            }
        }
        finally
        {
            if (TryBeginDisconnect())
            {
                CleanupConnection();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte[] lengthPrefix = await ReadExactlyAsync(4, cancellationToken);
        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);

        if (payloadLength < 0)
        {
            throw new InvalidOperationException("Protocol frame có độ dài âm.");
        }

        // Kiểm tra giới hạn độ dài payload để tránh OutOfMemoryException (Issue #61)
        if (payloadLength > ProtocolFrameCodec.MaxPayloadLength)
        {
            throw new InvalidOperationException($"Độ dài gói tin {payloadLength} bytes vượt quá giới hạn tối đa ({ProtocolFrameCodec.MaxPayloadLength} bytes).");
        }

        byte[] payload = await ReadExactlyAsync(payloadLength, cancellationToken);
        byte[] frame = new byte[4 + payloadLength];

        lengthPrefix.CopyTo(frame.AsSpan(0, 4));
        payload.CopyTo(frame.AsSpan(4));
        return frame;
    }

    private static async Task SendAllAsync(
        Socket socket,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        while (!data.IsEmpty)
        {
            int sent = await socket.SendAsync(
                data,
                SocketFlags.None,
                cancellationToken);

            if (sent == 0)
            {
                throw new IOException("Không gửi được dữ liệu tới server.");
            }

            data = data[sent..];
        }
    }

    private async Task<byte[]> ReadExactlyAsync(
        int length,
        CancellationToken cancellationToken)
    {
        Socket socket = _socket
            ?? throw new InvalidOperationException("Socket chưa được khởi tạo.");

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
                throw new IOException("Server đã đóng kết nối.");
            }

            offset += read;
        }

        return buffer;
    }

    private bool TryBeginDisconnect()
    {
        lock (_disconnectLock)
        {
            if (_isDisconnected)
            {
                return false;
            }

            _isDisconnected = true;
            return true;
        }
    }

    private void CleanupConnection()
    {
        _receiveLoopCts?.Dispose();
        _receiveLoopCts = null;

        _socket?.Dispose();
        _socket = null;
        _receiveLoopTask = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
    }
}
