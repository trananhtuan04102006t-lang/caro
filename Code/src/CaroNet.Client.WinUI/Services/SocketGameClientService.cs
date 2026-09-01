using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Client.WinUI.Models;
using CaroNet.Shared.Game;
using CaroNet.Shared.Protocol;
using CaroNet.Shared.Protocol.Payloads;

namespace CaroNet.Client.WinUI.Services;

public sealed class SocketGameClientService : IGameClientService, IAsyncDisposable
{
    private readonly List<(int Row, int Col)> _winningCells = new();
    public List<(int Row, int Col)> WinningCells => _winningCells;

    private const int BoardSize = 15;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly IClientConnection _connection;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _authRequestLock = new(1, 1);
    private readonly SemaphoreSlim _roomRequestLock = new(1, 1);
    private readonly SemaphoreSlim _historyRequestLock = new(1, 1);
    private readonly SemaphoreSlim _topRecordsRequestLock = new(1, 1);
    private TaskCompletionSource<AuthSession>? _authCompletion;
    private TaskCompletionSource<IReadOnlyList<MatchSummary>>? _historyCompletion;
    private TaskCompletionSource<IReadOnlyList<PlayerRecordSummary>>? _topRecordsCompletion;
    private TaskCompletionSource<GameViewState>? _roomJoinedCompletion;
    private string _connectionStatus = "Chưa kết nối server";
    private string _currentTurnSymbol = "X";
    private string _opponentName = "Đối thủ";
    private string _playerId = string.Empty;
    private string _playerName = "Player";
    private string _playerSymbol = "?";
    private string _roomId = string.Empty;
    private string _serverError = string.Empty;
    private bool _hasOpponent;
    private int _myScore;
    private int _opponentScore;
    private AuthSession? _currentAuth;
    private string[,] _board = InitEmptyBoard();

    // Định nghĩa sự kiện nhận Chat từ Socket mạng thật gửi về
    public event EventHandler<CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload>? ChatReceived;

    public event EventHandler<DrawOfferReceivedEventArgs>? DrawOfferReceived;

    // Hàm gửi tin nhắn qua Socket lên Server mạng thật
    public async Task SendChatAsync(string message)
    {
        var payload = new CaroNet.Shared.Protocol.Payloads.ChatPayload
        {
            Message = message
        };

        var envelope = new MessageEnvelope
        {
            Type = MessageType.Chat,
            RoomId = EmptyToNull(_roomId),
            PlayerId = EmptyToNull(_playerId),
            // Đồng bộ cách đóng gói dạng JsonElement giống như hàm MakeMoveAsync
            Payload = JsonSerializer.SerializeToElement(payload)
        };

        // Truyền kèm CancellationToken do hàm SendAsync của connection yêu cầu
        await _connection.SendAsync(envelope, CancellationToken.None);
    }

    public SocketGameClientService(IClientConnection connection)
    {
        _connection = connection;
        _connection.MessageReceived += Connection_MessageReceived;
        _connection.ConnectionError += Connection_ConnectionError;
        _connection.Disconnected += Connection_Disconnected;
    }

    private static string[,] InitEmptyBoard()
    {
        var board = new string[BoardSize, BoardSize];
        for (int r = 0; r < BoardSize; r++)
            for (int c = 0; c < BoardSize; c++)
                board[r, c] = string.Empty;
        return board;
    }

    public event EventHandler<GameViewState>? GameStateUpdated;

    public GameViewState CurrentState => BuildState();

    public AuthSession? CurrentAuth
    {
        get
        {
            lock (_stateLock)
            {
                return _currentAuth;
            }
        }
    }

    public async Task ConnectAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken)
    {
        _playerName = string.IsNullOrWhiteSpace(request.PlayerName)
            ? "Player"
            : request.PlayerName.Trim();

        await _connection.ConnectAsync(
            request.Host,
            request.Port,
            cancellationToken);

        _connectionStatus = $"Đã kết nối server {request.Host}:{request.Port}";
        _serverError = string.Empty;

        await _connection.SendAsync(
            new MessageEnvelope
            {
                Type = MessageType.Hello,
                Payload = JsonSerializer.SerializeToElement(new
                {
                    playerName = _playerName
                })
            },
            cancellationToken);

        PublishState();
    }

    public async Task<AuthSession> RegisterAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (!await _authRequestLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hệ thống đang xử lý đăng nhập trước đó. Vui lòng đợi.");
        }

        try
        {
            TaskCompletionSource<AuthSession> completion = PrepareAuthWaiter();

            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.Register,
                    Payload = JsonSerializer.SerializeToElement(new AuthRequestPayload
                    {
                        Username = username,
                        Password = password,
                        DisplayName = displayName
                    })
                },
                cancellationToken);

            return await WaitForAuthAsync(completion, cancellationToken);
        }
        finally
        {
            _authRequestLock.Release();
        }
    }

    public async Task<AuthSession> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        if (!await _authRequestLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hệ thống đang xử lý đăng nhập trước đó. Vui lòng đợi.");
        }

        try
        {
            TaskCompletionSource<AuthSession> completion = PrepareAuthWaiter();

            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.Login,
                    Payload = JsonSerializer.SerializeToElement(new AuthRequestPayload
                    {
                        Username = username,
                        Password = password
                    })
                },
                cancellationToken);

            return await WaitForAuthAsync(completion, cancellationToken);
        }
        finally
        {
            _authRequestLock.Release();
        }
    }

    public async Task<GameViewState> CreateRoomAsync(CancellationToken cancellationToken)
    {
        // Thử chiếm chốt chặn ngay lập tức (0ms). Nếu thất bại = đang có request chạy ngầm.
        if (!await _roomRequestLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hệ thống đang xử lý yêu cầu vào phòng trước đó. Vui lòng đợi.");
        }

        try
        {
            TaskCompletionSource<GameViewState> completion = PrepareRoomJoinWaiter();

            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.CreateRoom,
                    PlayerId = EmptyToNull(_playerId),
                    Payload = JsonSerializer.SerializeToElement(new { })
                },
                cancellationToken);

            return await WaitForRoomJoinedAsync(completion, cancellationToken);
        }
        finally
        {
            // Luôn luôn mở khóa chốt chặn khi kết thúc (kể cả thành công, thất bại hay timeout)
            _roomRequestLock.Release();
        }
    }

    public async Task<GameViewState> QuickMatchAsync(CancellationToken cancellationToken)
    {
        if (!await _roomRequestLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hệ thống đang xử lý yêu cầu vào phòng trước đó. Vui lòng đợi.");
        }

        try
        {
            TaskCompletionSource<GameViewState> completion = PrepareRoomJoinWaiter();

            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.QuickMatch,
                    PlayerId = EmptyToNull(_playerId),
                    Payload = JsonSerializer.SerializeToElement(new { })
                },
                cancellationToken);

            return await WaitForRoomJoinedAsync(completion, cancellationToken);
        }
        finally
        {
            _roomRequestLock.Release();
        }
    }

    public async Task<IReadOnlyList<MatchSummary>> GetMyHistoryAsync(
        CancellationToken cancellationToken)
    {
        if (!await _historyRequestLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hệ thống đang tải lịch sử. Vui lòng đợi.");
        }

        try
        {
            TaskCompletionSource<IReadOnlyList<MatchSummary>> completion = PrepareHistoryWaiter();

            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.MyHistoryRequest,
                    PlayerId = EmptyToNull(_playerId),
                    Payload = JsonSerializer.SerializeToElement(new { })
                },
                cancellationToken);

            return await WaitForHistoryAsync(completion, cancellationToken);
        }
        finally
        {
            _historyRequestLock.Release();
        }
    }

    public async Task<IReadOnlyList<PlayerRecordSummary>> GetTopRecordsAsync(
        CancellationToken cancellationToken)
    {
        if (!await _topRecordsRequestLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hệ thống đang tải bảng xếp hạng. Vui lòng đợi.");
        }

        try
        {
            TaskCompletionSource<IReadOnlyList<PlayerRecordSummary>> completion = PrepareTopRecordsWaiter();

            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.TopRecordsRequest,
                    PlayerId = EmptyToNull(_playerId),
                    Payload = JsonSerializer.SerializeToElement(new { })
                },
                cancellationToken);

            return await WaitForTopRecordsAsync(completion, cancellationToken);
        }
        finally
        {
            _topRecordsRequestLock.Release();
        }
    }

    public async Task<GameViewState> JoinRoomAsync(
        string roomId,
        CancellationToken cancellationToken)
    {
        // Thử chiếm chốt chặn ngay lập tức
        if (!await _roomRequestLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Hệ thống đang xử lý yêu cầu vào phòng trước đó. Vui lòng đợi.");
        }

        try
        {
            TaskCompletionSource<GameViewState> completion = PrepareRoomJoinWaiter();
            string trimmedRoomId = roomId.Trim();

            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.JoinRoom,
                    RoomId = trimmedRoomId,
                    PlayerId = EmptyToNull(_playerId),
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        roomId = trimmedRoomId
                    })
                },
                cancellationToken);

            return await WaitForRoomJoinedAsync(completion, cancellationToken);
        }
        finally
        {
            // Luôn luôn mở khóa chốt chặn khi kết thúc
            _roomRequestLock.Release();
        }
    }

    public async Task MakeMoveAsync(
        BoardPosition position,
        CancellationToken cancellationToken)
    {
        await _connection.SendAsync(
            new MessageEnvelope
            {
                Type = MessageType.MakeMove,
                RoomId = EmptyToNull(_roomId),
                PlayerId = EmptyToNull(_playerId),
                Payload = JsonSerializer.SerializeToElement(new
                {
                    row = position.Row,
                    column = position.Column,
                    playerId = _playerId
                })
            },
            cancellationToken);
    }

    public async Task SendResignAsync(CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(
            new MessageEnvelope
            {
                Type = MessageType.Resign,
                RoomId = EmptyToNull(_roomId),
                PlayerId = EmptyToNull(_playerId),
                Payload = JsonSerializer.SerializeToElement(new { })
            },
            cancellationToken);
    }

    public async Task SendDrawOfferAsync(CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(
            new MessageEnvelope
            {
                Type = MessageType.DrawOffer,
                RoomId = EmptyToNull(_roomId),
                PlayerId = EmptyToNull(_playerId),
                Payload = JsonSerializer.SerializeToElement(new { })
            },
            cancellationToken);
    }

    public async Task SendDrawResponseAsync(bool accepted, CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(
            new MessageEnvelope
            {
                Type = MessageType.DrawResponse,
                RoomId = EmptyToNull(_roomId),
                PlayerId = EmptyToNull(_playerId),
                Payload = JsonSerializer.SerializeToElement(new
                {
                    accepted
                })
            },
            cancellationToken);
    }

    public async Task LeaveRoomAsync(CancellationToken cancellationToken = default)
    {
        string roomId;
        string playerId;

        lock (_stateLock)
        {
            roomId = _roomId;
            playerId = _playerId;
        }

        if (!string.IsNullOrWhiteSpace(roomId))
        {
            await _connection.SendAsync(
                new MessageEnvelope
                {
                    Type = MessageType.LeaveRoom,
                    RoomId = EmptyToNull(roomId),
                    PlayerId = EmptyToNull(playerId),
                    Payload = JsonSerializer.SerializeToElement(new { })
                },
                cancellationToken);
        }

        ResetRoomState("Đã rời phòng.");
    }

    private TaskCompletionSource<GameViewState> PrepareRoomJoinWaiter()
    {
        var completion = new TaskCompletionSource<GameViewState>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_stateLock)
        {
            _roomJoinedCompletion = completion;
            _serverError = string.Empty;
        }

        return completion;
    }

    private TaskCompletionSource<AuthSession> PrepareAuthWaiter()
    {
        var completion = new TaskCompletionSource<AuthSession>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_stateLock)
        {
            _authCompletion = completion;
            _serverError = string.Empty;
        }

        return completion;
    }

    private TaskCompletionSource<IReadOnlyList<MatchSummary>> PrepareHistoryWaiter()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<MatchSummary>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_stateLock)
        {
            _historyCompletion = completion;
            _serverError = string.Empty;
        }

        return completion;
    }

    private TaskCompletionSource<IReadOnlyList<PlayerRecordSummary>> PrepareTopRecordsWaiter()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<PlayerRecordSummary>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_stateLock)
        {
            _topRecordsCompletion = completion;
            _serverError = string.Empty;
        }

        return completion;
    }

    private static async Task<GameViewState> WaitForRoomJoinedAsync(
        TaskCompletionSource<GameViewState> completion,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            return await completion.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Server chưa phản hồi yêu cầu phòng.");
        }
    }

    private static async Task<AuthSession> WaitForAuthAsync(
        TaskCompletionSource<AuthSession> completion,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            return await completion.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Server chưa phản hồi đăng nhập.");
        }
    }

    private static async Task<IReadOnlyList<MatchSummary>> WaitForHistoryAsync(
        TaskCompletionSource<IReadOnlyList<MatchSummary>> completion,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            return await completion.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Server chưa phản hồi lịch sử trận đấu.");
        }
    }

    private static async Task<IReadOnlyList<PlayerRecordSummary>> WaitForTopRecordsAsync(
        TaskCompletionSource<IReadOnlyList<PlayerRecordSummary>> completion,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            return await completion.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Server chưa phản hồi bảng xếp hạng.");
        }
    }

    private void Connection_MessageReceived(
        object? sender,
        ClientMessageReceivedEventArgs args)
    {
        try
        {
            switch (args.Message.Type)
            {
                case MessageType.HelloAccepted:
                    ApplyHelloAccepted(args.Message);
                    break;
                case MessageType.AuthAccepted:
                    ApplyAuthAccepted(args.Message);
                    break;
                case MessageType.RoomJoined:
                case MessageType.GameStarted:
                    ApplyRoomJoined(args.Message);
                    break;
                case MessageType.GameStateUpdated:
                    ApplyGameStateUpdated(args.Message);
                    break;
                case MessageType.MoveRejected:
                case MessageType.Error:
                    ApplyServerError(args.Message);
                    break;
                case MessageType.GameEnded:
                    ApplyGameEnded(args.Message);
                    break;
                case MessageType.RematchAccepted:
                    ApplyRematchAccepted(args.Message);
                    break;
                case MessageType.ChatReceived:
                    ApplyChatReceived(args.Message);
                    break;
                case MessageType.DrawOffer:
                    ApplyDrawOffer(args.Message);
                    break;
                case MessageType.MyHistoryReceived:
                    ApplyMyHistoryReceived(args.Message);
                    break;
                case MessageType.TopRecordsReceived:
                    ApplyTopRecordsReceived(args.Message);
                    break;
            }
        }
        catch (Exception ex)
        {
            UpdateError($"Không đọc được message từ server: {ex.Message}");
        }
    }

    private void ApplyHelloAccepted(MessageEnvelope message)
    {
        lock (_stateLock)
        {
            _playerId = FirstNonEmpty(
                GetString(message.Payload, "playerId"),
                message.PlayerId,
                _playerId);
            _connectionStatus = "Server đã chấp nhận kết nối.";
            _serverError = string.Empty;
        }

        PublishState();
    }

    private void ApplyAuthAccepted(MessageEnvelope message)
    {
        AuthSession session;

        lock (_stateLock)
        {
            session = new AuthSession(
                FirstNonEmpty(GetString(message.Payload, "userId")),
                FirstNonEmpty(GetString(message.Payload, "username")),
                FirstNonEmpty(GetString(message.Payload, "displayName"), _playerName));

            _currentAuth = session;
            _playerName = session.DisplayName;
            _playerId = FirstNonEmpty(message.PlayerId, _playerId);
            _connectionStatus = $"Đã đăng nhập: {session.DisplayName}";
            _serverError = string.Empty;
        }

        _authCompletion?.TrySetResult(session);
        PublishState();
    }

    private void ApplyRoomJoined(MessageEnvelope message)
    {
        lock (_stateLock)
        {
            _roomId = FirstNonEmpty(
                GetString(message.Payload, "roomId"),
                message.RoomId,
                _roomId);
            _playerId = FirstNonEmpty(
                GetString(message.Payload, "playerId"),
                message.PlayerId,
                _playerId);
            _playerSymbol = FirstNonEmpty(
                GetString(message.Payload, "playerSymbol"),
                GetString(message.Payload, "yourSymbol"),
                GetString(message.Payload, "symbol"),
                _playerSymbol);
            _opponentName = FirstNonEmpty(
                GetString(message.Payload, "opponentName"),
                _opponentName);
            _connectionStatus = string.IsNullOrWhiteSpace(_roomId)
                ? "Đã vào phòng"
                : $"Đã vào phòng {_roomId}";
            _serverError = string.Empty;
            _hasOpponent = message.Type == MessageType.GameStarted;

            if (TryReadBoard(message.Payload, out string[,]? board))
            {
                _board = board!;
            }
            _currentTurnSymbol = ResolveCurrentTurnSymbol(message.Payload);
        }

        GameViewState state = PublishState();
        _roomJoinedCompletion?.TrySetResult(state);
    }

    private void ApplyGameStateUpdated(MessageEnvelope message)
    {
        lock (_stateLock)
        {
            if (TryReadBoard(message.Payload, out string[,]? board))
            {
                _board = board!;
            }

            _currentTurnSymbol = ResolveCurrentTurnSymbol(message.Payload);
            _serverError = string.Empty;
        }

        PublishState();
    }

    private void ApplyServerError(MessageEnvelope message)
    {
        string error;

        lock (_stateLock)
        {
            error = FirstNonEmpty(
                GetString(message.Payload, "message"),
                GetString(message.Payload, "error"),
                GetString(message.Payload, "reason"),
                "Server từ chối yêu cầu.");

            _serverError = error;
        }

        _authCompletion?.TrySetException(new InvalidOperationException(error));
        _historyCompletion?.TrySetException(new InvalidOperationException(error));
        _topRecordsCompletion?.TrySetException(new InvalidOperationException(error));
        _roomJoinedCompletion?.TrySetException(new InvalidOperationException(error));
        PublishState();
    }

    private void ApplyGameEnded(MessageEnvelope message)
    {
        lock (_stateLock)
        {
            if (TryReadBoard(message.Payload, out string[,]? board))
            {
                _board = board!;
            }

            UpdateScore(GetString(message.Payload, "winnerPlayerId"));

            string reason = GetString(message.Payload, "reason");

            _serverError = reason == "opponent_disconnected"
                ? "Đối thủ đã ngắt kết nối. Bạn thắng!"
                : reason == "resigned" && GetString(message.Payload, "winnerPlayerId") == _playerId
                    ? "Đối thủ đầu hàng. Bạn thắng!"
                : reason == "resigned"
                    ? "Bạn đã đầu hàng. Bạn thua."
                : reason == "draw_agreed"
                    ? "Hai bên đã đồng ý hòa."
                : reason == "timeout" && GetString(message.Payload, "winnerPlayerId") == _playerId
                    ? "Đối thủ hết thời gian. Bạn thắng!"
                : reason == "timeout"
                    ? "Bạn hết thời gian. Bạn thua."
                : FirstNonEmpty(
                    GetString(message.Payload, "message"),
                    "Ván đấu đã kết thúc.");

            _connectionStatus = "Trò chơi kết thúc";
            _winningCells.Clear();
            if (message.Payload.HasValue &&
                TryGetProperty(message.Payload, "winningCells", out var winningProp) &&
                winningProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var cellElement in winningProp.EnumerateArray())
                {
                    int row = ReadInt(cellElement, "row", "Row");
                    int column = ReadInt(cellElement, "column", "Column", "col", "Col");
                    if (row >= 0 && column >= 0)
                    {
                        _winningCells.Add((row, column));
                    }
                }
            }
        }

        PublishState();
    }

    private void Connection_ConnectionError(object? sender, Exception exception)
    {
        UpdateError($"Lỗi kết nối: {exception.Message}");
    }

    private void Connection_Disconnected(object? sender, EventArgs args)
    {
        const string disconnectedMessage = "Mất kết nối server";

        lock (_stateLock)
        {
            _connectionStatus = disconnectedMessage;
            _currentAuth = null;
            _playerId = string.Empty;
        }

        _authCompletion?.TrySetException(new InvalidOperationException(disconnectedMessage));
        _historyCompletion?.TrySetException(new InvalidOperationException(disconnectedMessage));
        _topRecordsCompletion?.TrySetException(new InvalidOperationException(disconnectedMessage));
        _roomJoinedCompletion?.TrySetException(new InvalidOperationException(disconnectedMessage));
        PublishState();
    }

    private void UpdateConnectionStatus(string status)
    {
        lock (_stateLock)
        {
            _connectionStatus = status;
            _serverError = string.Empty;
        }

        PublishState();
    }

    private void UpdateError(string error)
    {
        lock (_stateLock)
        {
            _serverError = error;
        }

        _authCompletion?.TrySetException(new InvalidOperationException(error));
        _historyCompletion?.TrySetException(new InvalidOperationException(error));
        _topRecordsCompletion?.TrySetException(new InvalidOperationException(error));
        _roomJoinedCompletion?.TrySetException(new InvalidOperationException(error));
        PublishState();
    }

    private void ResetRoomState(string connectionStatus)
    {
        lock (_stateLock)
        {
            _roomId = string.Empty;
            _playerSymbol = "?";
            _currentTurnSymbol = "X";
            _opponentName = "Đối thủ";
            _hasOpponent = false;
            _serverError = string.Empty;
            _connectionStatus = connectionStatus;
            _myScore = 0;
            _opponentScore = 0;
            _winningCells.Clear();
            _board = InitEmptyBoard();
        }

        PublishState();
    }

    private GameViewState PublishState()
    {
        GameViewState state = BuildState();
        GameStateUpdated?.Invoke(this, state);
        return state;
    }

    private GameViewState BuildState()
    {
        lock (_stateLock)
        {
            return new GameViewState(
                _roomId,
                _playerName,
                _playerSymbol,
                _currentTurnSymbol,
                _connectionStatus,
                _serverError,
                BuildCells(),
                _opponentName,
                _myScore,
                _opponentScore,
                _hasOpponent,
                _playerId);
        }
    }

    private IReadOnlyList<CellViewState> BuildCells()
    {
        var cells = new List<CellViewState>(BoardSize * BoardSize);

        for (var row = 0; row < BoardSize; row++)
        {
            for (var column = 0; column < BoardSize; column++)
            {
                cells.Add(new CellViewState(row, column, _board[row, column]));
            }
        }

        return cells;
    }

    private string ResolveCurrentTurnSymbol(JsonElement? payload)
    {
        string explicitSymbol = GetString(payload, "currentTurnSymbol");
        if (!string.IsNullOrWhiteSpace(explicitSymbol))
        {
            return explicitSymbol;
        }

        string currentTurnPlayerId = GetString(payload, "currentTurnPlayerId");
        if (string.IsNullOrWhiteSpace(currentTurnPlayerId))
        {
            return _currentTurnSymbol;
        }

        return currentTurnPlayerId == _playerId
            ? _playerSymbol
            : GetOpponentSymbol(_playerSymbol);
    }

    private static bool TryReadBoard(
        JsonElement? payload,
        out string[,]? board)
    {
        board = null;

        if (!TryGetProperty(payload, "board", out JsonElement boardElement) ||
            boardElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsedBoard = new string[BoardSize, BoardSize];
        var rowIndex = 0;

        foreach (JsonElement rowElement in boardElement.EnumerateArray().Take(BoardSize))
        {
            if (rowElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var columnIndex = 0;
            foreach (JsonElement cellElement in rowElement.EnumerateArray().Take(BoardSize))
            {
                parsedBoard[rowIndex, columnIndex] = cellElement.ValueKind == JsonValueKind.String
                    ? cellElement.GetString() ?? string.Empty
                    : string.Empty;
                columnIndex++;
            }

            rowIndex++;
        }

        board = parsedBoard;
        return true;
    }

    private static string GetString(JsonElement? payload, string propertyName)
    {
        if (!TryGetProperty(payload, propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return property.GetString() ?? string.Empty;
    }

    private static bool TryGetProperty(
        JsonElement? payload,
        string propertyName,
        out JsonElement property)
    {
        property = default;

        if (payload is not { ValueKind: JsonValueKind.Object } objectPayload)
        {
            return false;
        }

        foreach (JsonProperty jsonProperty in objectPayload.EnumerateObject())
        {
            if (string.Equals(
                    jsonProperty.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                property = jsonProperty.Value;
                return true;
            }
        }

        return false;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string GetOpponentSymbol(string playerSymbol)
    {
        return playerSymbol == "X" ? "O" : "X";
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async ValueTask DisposeAsync()
    {
        _authRequestLock.Dispose();
        _roomRequestLock.Dispose();
        _historyRequestLock.Dispose();
        _topRecordsRequestLock.Dispose();

        await _connection.DisposeAsync();
    }

    private void ApplyRematchAccepted(MessageEnvelope message)
    {
        lock (_stateLock)
        {
            _winningCells.Clear();

            _playerSymbol = FirstNonEmpty(
                GetString(message.Payload, "playerSymbol"),
                GetString(message.Payload, "yourSymbol"),
                GetString(message.Payload, "symbol"),
                _playerSymbol);
            _opponentName = FirstNonEmpty(
                GetString(message.Payload, "opponentName"),
                _opponentName);

            _currentTurnSymbol = ResolveCurrentTurnSymbol(message.Payload);
            _board = InitEmptyBoard();
            _serverError = string.Empty;
            _hasOpponent = true;
            _connectionStatus = "Trận đấu mới đã bắt đầu!";
        }

        PublishState();
    }

    public async Task SendRematchRequestAsync(CancellationToken cancellationToken = default)
    {
        await _connection.SendAsync(
            new MessageEnvelope
            {
                Type = MessageType.Rematch,
                RoomId = EmptyToNull(_roomId),
                PlayerId = EmptyToNull(_playerId),
                Payload = JsonSerializer.SerializeToElement(new { })
            },
            cancellationToken);
    }

    private void ApplyChatReceived(MessageEnvelope message)
    {
        if (message.Payload.HasValue)
        {
            try
            {
                var chatReceivedPayload = message.Payload.Value.Deserialize<CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload>();
                if (chatReceivedPayload != null)
                {
                    // Kích hoạt Event để GameViewModel bên ngoài nghe thấy và render lên giao diện
                    ChatReceived?.Invoke(this, chatReceivedPayload);
                }
            }
            catch (Exception ex)
            {
                UpdateError($"Không thể giải mã tin nhắn chat: {ex.Message}");
            }
        }
    }

    private void ApplyDrawOffer(MessageEnvelope message)
    {
        string senderPlayerId = GetString(message.Payload, "senderPlayerId");
        string senderName = FirstNonEmpty(
            GetString(message.Payload, "senderName"),
            "Đối thủ");

        DrawOfferReceived?.Invoke(
            this,
            new DrawOfferReceivedEventArgs(senderPlayerId, senderName));
    }

    private void ApplyMyHistoryReceived(MessageEnvelope message)
    {
        try
        {
            MyHistoryReceivedPayload? payload = message.Payload.HasValue
                ? message.Payload.Value.Deserialize<MyHistoryReceivedPayload>()
                : null;

            IReadOnlyList<MatchSummary> matches = payload?.Matches
                .Select(match => new MatchSummary
                {
                    PlayerX = match.PlayerXName,
                    PlayerO = match.PlayerOName,
                    Winner = string.IsNullOrWhiteSpace(match.WinnerName) ? "Hòa" : match.WinnerName!,
                    PlayedAt = match.PlayedAtUtc,
                    MoveCount = match.MoveCount
                })
                .ToList() ?? [];

            _historyCompletion?.TrySetResult(matches);
        }
        catch (Exception ex)
        {
            _historyCompletion?.TrySetException(
                new InvalidOperationException($"Không thể đọc lịch sử trận đấu: {ex.Message}", ex));
        }
    }

    private void ApplyTopRecordsReceived(MessageEnvelope message)
    {
        try
        {
            TopRecordsReceivedPayload? payload = message.Payload.HasValue
                ? message.Payload.Value.Deserialize<TopRecordsReceivedPayload>()
                : null;

            IReadOnlyList<PlayerRecordSummary> records = payload?.Players
                .Select(player => new PlayerRecordSummary
                {
                    PlayerName = player.PlayerName,
                    Wins = player.Wins,
                    Losses = player.Losses,
                    Draws = player.Draws
                })
                .ToList() ?? [];

            _topRecordsCompletion?.TrySetResult(records);
        }
        catch (Exception ex)
        {
            _topRecordsCompletion?.TrySetException(
                new InvalidOperationException($"Không thể đọc bảng xếp hạng: {ex.Message}", ex));
        }
    }

    private static int ReadInt(JsonElement payload, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (payload.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetInt32(out int value))
            {
                return value;
            }
        }

        return -1;
    }

    private void UpdateScore(string winnerPlayerId)
    {
        if (string.IsNullOrWhiteSpace(winnerPlayerId))
        {
            return;
        }

        if (winnerPlayerId == _playerId)
        {
            _myScore++;
        }
        else
        {
            _opponentScore++;
        }
    }
}
