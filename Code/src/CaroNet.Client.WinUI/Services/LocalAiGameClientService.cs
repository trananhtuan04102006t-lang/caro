using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Client.WinUI.Models;
using CaroNet.Shared.Game;
using CaroNet.Shared.Game.AI;
using CaroNet.Shared.Protocol.Payloads;

namespace CaroNet.Client.WinUI.Services;

/// <summary>
/// Game service cục bộ cho chế độ Người vs Máy.
/// UI vẫn dùng IGameClientService như chế độ online, nhưng đối thủ được điều khiển
/// bởi Minimax + Alpha-Beta chạy trên máy người dùng.
/// </summary>
public sealed class LocalAiGameClientService : IGameClientService, IDisposable
{
    private const int BoardSize = 15;

    private readonly CaroGameState _gameState = new(BoardSize);
    private readonly ICaroAiPlayer _aiPlayer;
    private readonly SemaphoreSlim _moveLock = new(1, 1);
    private readonly AuthSession? _auth;
    private readonly string _playerName;
    private readonly AiDifficulty _difficulty;

    private string _roomId = string.Empty;
    private string _connectionStatus = "Chế độ Người vs Máy";
    private string _serverError = string.Empty;
    private GameViewState? _currentState;
    private bool _hasOpponent;

    public LocalAiGameClientService(
        string playerName,
        AuthSession? auth,
        AiDifficulty difficulty)
    {
        _playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
        _auth = auth;
        _difficulty = difficulty;
        _aiPlayer = new MinimaxAiPlayer(difficulty);
    }

    public event EventHandler<ChatReceivedPayload>? ChatReceived;
    public event EventHandler<DrawOfferReceivedEventArgs>? DrawOfferReceived;
    public event EventHandler<GameViewState>? GameStateUpdated;

    public GameViewState CurrentState => _currentState ?? BuildState(string.Empty);
    public AuthSession? CurrentAuth => _auth;

    public Task SendChatAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Task.CompletedTask;

        ChatReceived?.Invoke(this, new ChatReceivedPayload
        {
            SenderName = _playerName,
            Message = message.Trim(),
            Timestamp = DateTime.Now
        });

        return Task.CompletedTask;
    }

    public Task ConnectAsync(ConnectionRequest request, CancellationToken cancellationToken)
    {
        _connectionStatus = "Đã sẵn sàng chế độ Người vs Máy (local)";
        PublishState(string.Empty);
        return Task.CompletedTask;
    }

    public Task<AuthSession> RegisterAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Đăng ký tài khoản được thực hiện ở chế độ online.");
    }

    public Task<AuthSession> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Đăng nhập được thực hiện ở chế độ online.");
    }

    public Task<GameViewState> CreateRoomAsync(CancellationToken cancellationToken)
    {
        StartLocalMatch();
        return Task.FromResult(PublishState(string.Empty));
    }

    public Task<GameViewState> JoinRoomAsync(string roomId, CancellationToken cancellationToken)
    {
        StartLocalMatch();
        return Task.FromResult(PublishState(string.Empty));
    }

    public Task<GameViewState> QuickMatchAsync(CancellationToken cancellationToken)
    {
        StartLocalMatch();
        return Task.FromResult(PublishState(string.Empty));
    }

    public Task<IReadOnlyList<MatchSummary>> GetMyHistoryAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<MatchSummary>>([]);

    public Task<IReadOnlyList<PlayerRecordSummary>> GetTopRecordsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PlayerRecordSummary>>([]);

    public async Task MakeMoveAsync(BoardPosition position, CancellationToken cancellationToken)
    {
        await _moveLock.WaitAsync(cancellationToken);

        try
        {
            _serverError = string.Empty;

            MoveResult humanResult = _gameState.MakeMove(position, PlayerSymbol.X);
            if (!humanResult.IsSuccess)
            {
                PublishState(ToMoveError(humanResult.Reason));
                return;
            }

            if (_gameState.Status != GameStatus.Playing)
            {
                SetTerminalMessage();
                PublishState(_serverError);
                return;
            }

            PublishState(string.Empty);

            _connectionStatus = $"AI đang suy nghĩ ({DifficultyText(_difficulty)})...";
            PublishState(string.Empty);

            BoardPosition aiMove = await Task.Run(
                () => _aiPlayer.FindBestMove(
                    _gameState,
                    PlayerSymbol.O,
                    cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            MoveResult aiResult = _gameState.MakeMove(aiMove, PlayerSymbol.O);
            if (!aiResult.IsSuccess)
            {
                PublishState($"AI không thể thực hiện nước đi: {aiResult.Reason}");
                return;
            }

            if (_gameState.Status != GameStatus.Playing)
            {
                SetTerminalMessage();
                PublishState(_serverError);
            }
            else
            {
                _connectionStatus = $"AI đã đánh {aiMove.Row + 1},{aiMove.Column + 1}";
                PublishState(string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            _connectionStatus = "Đã hủy lượt AI.";
            PublishState(string.Empty);
        }
        finally
        {
            _moveLock.Release();
        }
    }

    public Task SendResignAsync(CancellationToken cancellationToken = default)
    {
        if (_gameState.Status == GameStatus.Playing)
        {
            _gameState.EndByResignation(PlayerSymbol.O);
            _connectionStatus = "Trò chơi kết thúc";
            _serverError = "Bạn đã đầu hàng. AI thắng.";
            PublishState(_serverError);
        }

        return Task.CompletedTask;
    }

    public Task SendDrawOfferAsync(CancellationToken cancellationToken = default)
    {
        ChatReceived?.Invoke(this, new ChatReceivedPayload
        {
            SenderName = "Hệ thống AI",
            Message = "Chế độ Người vs Máy không hỗ trợ thương lượng hòa.",
            Timestamp = DateTime.Now
        });

        return Task.CompletedTask;
    }

    public Task SendDrawResponseAsync(bool accepted, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SendRematchRequestAsync(CancellationToken cancellationToken = default)
    {
        _gameState.Reset();
        _roomId = "AI-ROOM-001";
        _hasOpponent = true;
        _connectionStatus = $"Trận đấu mới với AI ({DifficultyText(_difficulty)})";
        PublishState(string.Empty);
        return Task.CompletedTask;
    }

    public Task LeaveRoomAsync(CancellationToken cancellationToken = default)
    {
        _gameState.Reset();
        _roomId = string.Empty;
        _hasOpponent = false;
        _connectionStatus = "Đã rời chế độ Người vs Máy.";
        PublishState(string.Empty);
        return Task.CompletedTask;
    }

    public void Dispose() => _moveLock.Dispose();

    private void SetTerminalMessage()
    {
        _connectionStatus = "Trò chơi kết thúc";
        _serverError = _gameState.Status switch
        {
            GameStatus.XWon => "Bạn thắng!",
            GameStatus.OWon => "AI thắng!",
            GameStatus.Draw => "Ván đấu hòa.",
            _ => "Ván đấu đã kết thúc."
        };
    }

    private void StartLocalMatch()
    {
        _gameState.Reset();
        _roomId = "AI-ROOM-001";
        _hasOpponent = true;
        _connectionStatus = $"Người vs Máy - Minimax + Alpha-Beta ({DifficultyText(_difficulty)})";
        _serverError = string.Empty;
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
        var cells = new List<CellViewState>(BoardSize * BoardSize);

        for (int row = 0; row < BoardSize; row++)
        {
            for (int col = 0; col < BoardSize; col++)
            {
                string mark = _gameState[row, col] switch
                {
                    CellState.X => "X",
                    CellState.O => "O",
                    _ => string.Empty
                };

                cells.Add(new CellViewState(row, col, mark));
            }
        }

        string effectiveError = string.IsNullOrWhiteSpace(serverError)
            ? _serverError
            : serverError;

        return new GameViewState(
            RoomId: _roomId,
            PlayerName: _playerName,
            PlayerSymbol: "X",
            CurrentTurnSymbol: _gameState.CurrentPlayer.ToString(),
            ConnectionStatus: _connectionStatus,
            ServerError: effectiveError,
            Cells: cells,
            OpponentName: $"AI Minimax ({DifficultyText(_difficulty)})",
            MyScore: 0,
            OpponentScore: 0,
            HasOpponent: _hasOpponent,
            PlayerId: _auth?.UserId ?? "local-player");
    }

    private static string ToMoveError(MoveRejectReason reason) => reason switch
    {
        MoveRejectReason.CellOccupied => "Ô này đã được đánh.",
        MoveRejectReason.WrongTurn => "Chưa tới lượt của bạn.",
        MoveRejectReason.GameEnded => "Ván đấu đã kết thúc.",
        MoveRejectReason.OutOfBounds => "Nước đi nằm ngoài bàn cờ.",
        _ => "Nước đi không hợp lệ."
    };

    private static string DifficultyText(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => "Dễ",
        AiDifficulty.Medium => "Trung bình",
        AiDifficulty.Hard => "Khó",
        AiDifficulty.Expert => "Chuyên gia",
        _ => "Trung bình"
    };
}
