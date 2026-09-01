using System.Net;
using System.Net.Sockets;
using CaroNet.Server.Host.Services;

namespace CaroNet.Server.Host.Networking;

public sealed class SocketServer
{
    private const int ListenPort = 5000;

    private readonly ClientSessionRegistry _registry;
    private readonly IMessageDispatcher _dispatcher;

    private Socket? _listener;

    public SocketServer(
        ClientSessionRegistry registry,
        IMessageDispatcher dispatcher)
    {
        _registry = registry;
        _dispatcher = dispatcher;
    }

    public async Task RunAsync(
        CancellationToken cancellationToken)
    {
        _listener = new Socket(
            AddressFamily.InterNetwork, // Ipv4
            SocketType.Stream,
            ProtocolType.Tcp); // TCP Socket

        try
        {
            _listener.Bind(
                new IPEndPoint(
                    IPAddress.Any, // Lắng nghe trên mọi card mạng
                    ListenPort)); // Port 5000

            _listener.Listen(100); // Hàng chờ tối đa 100

            Console.WriteLine(
                $"[SERVER] Listening on port {ListenPort}");

            while (!cancellationToken.IsCancellationRequested)
            {
                Socket socket;

                try
                {
                    socket =
                        await _listener.AcceptAsync(
                            cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var session =
                    new ClientSession(
                        socket,
                        _dispatcher);

                _registry.Add(session);

                Console.WriteLine(
                    $"[SERVER] Client connected. Online={_registry.Count}");

                // Không truyền cancellationToken vào Task.Run để tránh leak session
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await session.RunAsync(cancellationToken);
                    }
                    finally
                    {
                        _registry.Remove(session.Id);

                        // Dọn room khi client ngắt kết nối
                        if (_dispatcher is GameMessageDispatcher gameDispatcher)
                        {
                            await gameDispatcher.HandleDisconnectAsync(session.Id);
                        }

                        Console.WriteLine(
                            $"[SERVER] Client removed. Online={_registry.Count}");
                    }
                });
            }
        }
        finally
        {
            // Giải phóng port ngay khi dừng server
            _listener.Dispose();

            Console.WriteLine(
                "[SERVER] Listener socket closed.");
        }
    }
}
