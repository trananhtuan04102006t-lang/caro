using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Client.WinUI.Models;
using CaroNet.Shared.Game;

namespace CaroNet.Client.WinUI.Services;

public sealed class LocalDemoGameClientService : IGameClientService
{
    private const int BoardSize = 15;

    private readonly string[,] _board = new string[BoardSize, BoardSize];
    private string _connectionStatus = "Chưa kết nối server";
    private string _currentTurnSymbol = "X";
    private string _playerName = "Player";
    private string _playerSymbol = "X";
    private string _roomId = string.Empty;
    private bool _hasOpponent;
    private GameViewState? _currentState;
    private AuthSession? _currentAuth;

    // Đăng ký sự kiện Chat cho class demo
    public event EventHandler<CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload>? ChatReceived;

#pragma warning disable CS0067
    public event EventHandler<DrawOfferReceivedEventArgs>? DrawOfferReceived;
#pragma warning restore CS0067

    // Xử lý gửi chat ảo khi chạy Demo không có mạng
    public async Task SendChatAsync(string message)
    {
        // Giả lập độ trễ mạng 500ms rồi tự động phản hồi lại tin nhắn
        await Task.Delay(500);

        ChatReceived?.Invoke(this, new CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload
        {
            SenderName = "Hệ thống (Demo)",
            Message = $"Bạn vừa nói: {message}",
            Timestamp = DateTime.Now
        });
    }

    public event EventHandler<GameViewState>? GameStateUpdated;

    public GameViewState CurrentState => _currentState ?? BuildState(string.Empty);

    public AuthSession? CurrentAuth => _currentAuth;

    public Task ConnectAsync(ConnectionRequest request, CancellationToken cancellationToken)
    {
        _playerName = string.IsNullOrWhiteSpace(request.PlayerName) ? "Player" : request.PlayerName.Trim();
        _connectionStatus = $"Đã kết nối demo tới {request.Host}:{request.Port}";
        PublishState(string.Empty);
        return Task.CompletedTask;
    }

    public Task<AuthSession> RegisterAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        _currentAuth = new AuthSession(
            Guid.NewGuid().ToString(),
            username.Trim(),
            displayName.Trim());
        _playerName = _currentAuth.DisplayName;
        PublishState(string.Empty);

        return Task.FromResult(_currentAuth);
    }

    public Task<AuthSession> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        _currentAuth = new AuthSession(
            Guid.NewGuid().ToString(),
            username.Trim(),
            username.Trim());
        _playerName = _currentAuth.DisplayName;
        PublishState(string.Empty);

        return Task.FromResult(_currentAuth);
    }

    public Task<GameViewState> CreateRoomAsync(CancellationToken cancellationToken)
    {
        _roomId = "ROOM-001";
        _playerSymbol = "X";
        _hasOpponent = false;
        _connectionStatus = $"Đã tạo phòng {_roomId}";
        var state = PublishState(string.Empty);
        return Task.FromResult(state);
    }

    public Task<GameViewState> JoinRoomAsync(string roomId, CancellationToken cancellationToken)
    {
        _roomId = string.IsNullOrWhiteSpace(roomId) ? "ROOM-001" : roomId.Trim();
        _playerSymbol = "O";
        _hasOpponent = true;
        _connectionStatus = $"Đã vào phòng {_roomId}";
        var state = PublishState(string.Empty);
        return Task.FromResult(state);
    }

    public Task<GameViewState> QuickMatchAsync(CancellationToken cancellationToken)
    {
        _roomId = "ROOM-QUICK";
        _playerSymbol = "X";
        _hasOpponent = false;
        _connectionStatus = $"Đã vào phòng {_roomId}";
        var state = PublishState(string.Empty);

        return Task.FromResult(state);
    }

    public Task<IReadOnlyList<MatchSummary>> GetMyHistoryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MatchSummary> matches = [];
        return Task.FromResult(matches);
    }

    public Task<IReadOnlyList<PlayerRecordSummary>> GetTopRecordsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlayerRecordSummary> records =
        [
            new PlayerRecordSummary
            {
                PlayerName = "Demo",
                Wins = 1,
                Losses = 0,
                Draws = 0
            }
        ];

        return Task.FromResult(records);
    }

    public Task MakeMoveAsync(BoardPosition position, CancellationToken cancellationToken)
    {
        if (position.Row < 0 || position.Row >= BoardSize || position.Column < 0 || position.Column >= BoardSize)
        {
            PublishState("MoveRejected: nước đi ngoài bàn cờ.");
            return Task.CompletedTask;
        }

        if (_playerSymbol != _currentTurnSymbol)
        {
            PublishState("MoveRejected: chưa tới lượt của bạn.");
            return Task.CompletedTask;
        }

        if (!string.IsNullOrEmpty(_board[position.Row, position.Column]))
        {
            PublishState("MoveRejected: ô này đã được đánh.");
            return Task.CompletedTask;
        }

        _board[position.Row, position.Column] = _playerSymbol;
        _currentTurnSymbol = _currentTurnSymbol == "X" ? "O" : "X";
        PublishState(string.Empty);
        return Task.CompletedTask;
    }

    public Task SendResignAsync(CancellationToken cancellationToken = default)
    {
        PublishState("Bạn đã đầu hàng.");
        return Task.CompletedTask;
    }

    public Task SendDrawOfferAsync(CancellationToken cancellationToken = default)
    {
        ChatReceived?.Invoke(this, new CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload
        {
            SenderName = "Hệ thống (Demo)",
            Message = "Bạn đã gửi lời xin hòa.",
            Timestamp = DateTime.Now
        });

        return Task.CompletedTask;
    }

    public Task SendDrawResponseAsync(bool accepted, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SendRematchRequestAsync(CancellationToken cancellationToken = default)
    {
        Array.Clear(_board, 0, _board.Length);
        _currentTurnSymbol = "X";
        _hasOpponent = true;
        _connectionStatus = "Trận đấu mới đã bắt đầu!";
        PublishState(string.Empty);
        return Task.CompletedTask;
    }

    public Task LeaveRoomAsync(CancellationToken cancellationToken = default)
    {
        Array.Clear(_board, 0, _board.Length);
        _roomId = string.Empty;
        _playerSymbol = "?";
        _currentTurnSymbol = "X";
        _hasOpponent = false;
        _connectionStatus = "Đã rời phòng.";
        PublishState(string.Empty);
        return Task.CompletedTask;
    }

    private GameViewState PublishState(string serverError)
    {
        var state = BuildState(serverError);
        _currentState = state;
        GameStateUpdated?.Invoke(this, state);
        return state;
    }

    private GameViewState BuildState(string serverError)
    {
        var state = new GameViewState(
            _roomId,
            _playerName,
            _playerSymbol,
            _currentTurnSymbol,
            _connectionStatus,
            serverError,
            BuildCells(),
            HasOpponent: _hasOpponent,
            PlayerId: "demo-player");

        return state;
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
}
