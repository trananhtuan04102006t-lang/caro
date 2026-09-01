using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Client.WinUI.Services;
using CaroNet.Shared.Game;

namespace CaroNet.Client.WinUI.ViewModels;

public sealed class GameViewModel : INotifyPropertyChanged
{
    public const int BoardSize = 15;

    private readonly IGameClientService _gameClient;
    private readonly SynchronizationContext? _syncContext;
    private Action<Action>? _dispatchToUI;

    private string _connectionStatus = "Chưa kết nối server";
    private string _currentTurnSymbol = "X";
    private string _opponentName = "Đối thủ";
    private string _playerId = string.Empty;
    private string _playerName = "Player";
    private string _playerSymbol = "?";
    private string _roomId = string.Empty;
    private string _serverError = string.Empty;
    private bool _hasOpponent;
    private bool _isGameEnded;
    private bool _hasPendingRematchRequest;
    private bool _hasRequestedRematch;
    private string _chatInputText = string.Empty;
    private int _myScore;
    private int _opponentScore;
    private BoardPosition? _lastMovePosition;

    public GameViewModel(IGameClientService gameClient)
    {
        _gameClient = gameClient;
        _syncContext = SynchronizationContext.Current;

        _gameClient.GameStateUpdated += GameClient_GameStateUpdated;
        _gameClient.ChatReceived += GameClient_ChatReceived;
        _gameClient.DrawOfferReceived += GameClient_DrawOfferReceived;

        for (var row = 0; row < BoardSize; row++)
        {
            for (var column = 0; column < BoardSize; column++)
            {
                BoardCells.Add(new BoardCellViewModel(row, column));
            }
        }

        ApplyState(_gameClient.CurrentState);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DrawOfferReceivedEventArgs>? DrawOfferReceived;

    public ObservableCollection<BoardCellViewModel> BoardCells { get; } = [];

    public ObservableCollection<ChatMessageViewModel> ChatMessages { get; } = [];

    public string ChatInputText
    {
        get => _chatInputText;
        set
        {
            SetProperty(ref _chatInputText, value);
            OnPropertyChanged(nameof(IsSendButtonEnabled));
        }
    }

    public bool IsChatInputEnabled => !string.IsNullOrEmpty(RoomId);

    public bool IsSendButtonEnabled =>
        IsChatInputEnabled &&
        !string.IsNullOrWhiteSpace(ChatInputText) &&
        ChatInputText.Length <= 200;

    public string RoomId
    {
        get => _roomId;
        private set
        {
            if (SetProperty(ref _roomId, value))
            {
                OnPropertyChanged(nameof(CanUseMatchActions));
                OnPropertyChanged(nameof(CanRequestRematch));
            }
        }
    }

    public string PlayerName
    {
        get => _playerName;
        private set => SetProperty(ref _playerName, value);
    }

    public string OpponentName
    {
        get => _opponentName;
        private set => SetProperty(ref _opponentName, value);
    }

    public bool HasOpponent
    {
        get => _hasOpponent;
        private set
        {
            if (SetProperty(ref _hasOpponent, value))
            {
                OnPropertyChanged(nameof(CanUseMatchActions));
                OnPropertyChanged(nameof(CanRequestRematch));
            }
        }
    }

    public string PlayerSymbol
    {
        get => _playerSymbol;
        private set
        {
            if (SetProperty(ref _playerSymbol, value))
            {
                OnPropertyChanged(nameof(OpponentSymbol));
            }
        }
    }

    public string OpponentSymbol => PlayerSymbol switch
    {
        "X" => "O",
        "O" => "X",
        _ => "?"
    };

    public string CurrentTurnSymbol
    {
        get => _currentTurnSymbol;
        private set => SetProperty(ref _currentTurnSymbol, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    public string ServerError
    {
        get => _serverError;
        set
        {
            if (SetProperty(ref _serverError, value))
            {
                OnPropertyChanged(nameof(TurnMessage));
            }
        }
    }

    public int MyScore
    {
        get => _myScore;
        private set
        {
            if (SetProperty(ref _myScore, value))
            {
                OnPropertyChanged(nameof(ScoreText));
            }
        }
    }

    public int OpponentScore
    {
        get => _opponentScore;
        private set
        {
            if (SetProperty(ref _opponentScore, value))
            {
                OnPropertyChanged(nameof(ScoreText));
            }
        }
    }

    public string ScoreText => $"{MyScore} - {OpponentScore}";

    public bool IsGameEnded
    {
        get => _isGameEnded;
        private set
        {
            if (SetProperty(ref _isGameEnded, value))
            {
                OnPropertyChanged(nameof(CanUseMatchActions));
                OnPropertyChanged(nameof(MyHeaderOpacity));
                OnPropertyChanged(nameof(OpponentHeaderOpacity));
                OnPropertyChanged(nameof(MyTurnLabel));
                OnPropertyChanged(nameof(OpponentTurnLabel));
                OnPropertyChanged(nameof(TurnMessage));
                OnPropertyChanged(nameof(CanRequestRematch));
                OnPropertyChanged(nameof(RematchHint));
            }
        }
    }

    public BoardPosition? LastMovePosition
    {
        get => _lastMovePosition;
        private set => SetProperty(ref _lastMovePosition, value);
    }

    public bool IsMyTurn =>
        !string.IsNullOrWhiteSpace(RoomId) &&
        HasOpponent &&
        !string.IsNullOrWhiteSpace(PlayerSymbol) &&
        PlayerSymbol != "?" &&
        CurrentTurnSymbol == PlayerSymbol;

    public bool IsOpponentTurn =>
        !string.IsNullOrWhiteSpace(RoomId) &&
        HasOpponent &&
        !string.IsNullOrWhiteSpace(OpponentSymbol) &&
        OpponentSymbol != "?" &&
        CurrentTurnSymbol == OpponentSymbol;

    public bool CanUseMatchActions =>
        !string.IsNullOrWhiteSpace(RoomId) &&
        HasOpponent &&
        !IsGameEnded;

    public bool HasPendingRematchRequest
    {
        get => _hasPendingRematchRequest;
        private set
        {
            if (SetProperty(ref _hasPendingRematchRequest, value))
            {
                OnPropertyChanged(nameof(RematchActionText));
                OnPropertyChanged(nameof(RematchHint));
            }
        }
    }

    public bool HasRequestedRematch
    {
        get => _hasRequestedRematch;
        private set
        {
            if (SetProperty(ref _hasRequestedRematch, value))
            {
                OnPropertyChanged(nameof(CanRequestRematch));
                OnPropertyChanged(nameof(RematchActionText));
                OnPropertyChanged(nameof(RematchHint));
            }
        }
    }

    public bool CanRequestRematch =>
        !string.IsNullOrWhiteSpace(RoomId) &&
        HasOpponent &&
        IsGameEnded &&
        !HasRequestedRematch;

    public string RematchActionText => HasPendingRematchRequest
        ? "Chấp nhận chơi lại"
        : "Chơi lại";

    public string RematchHint
    {
        get
        {
            if (!IsGameEnded)
            {
                return string.Empty;
            }

            if (HasRequestedRematch)
            {
                return "Đang chờ đối thủ xác nhận chơi lại...";
            }

            if (HasPendingRematchRequest)
            {
                return "Đối thủ muốn chơi lại. Bạn có thể chấp nhận hoặc tiếp tục xem bàn cờ.";
            }

            return "Ván đã kết thúc. Bạn có thể chơi lại hoặc về menu.";
        }
    }

    public double MyHeaderOpacity => IsMyTurn ? 1.0 : 0.72;

    public double OpponentHeaderOpacity => IsOpponentTurn ? 1.0 : 0.72;

    public string MyTurnLabel => IsGameEnded
        ? "KẾT THÚC"
        : IsMyTurn ? "ĐẾN LƯỢT" : string.Empty;

    public string OpponentTurnLabel => IsGameEnded
        ? "KẾT THÚC"
        : IsOpponentTurn ? "ĐẾN LƯỢT" : string.Empty;

    public string TurnMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RoomId) ||
                string.IsNullOrWhiteSpace(PlayerSymbol) ||
                PlayerSymbol == "?")
            {
                return "Đang chờ đối thủ...";
            }

            if (IsGameEnded)
            {
                return string.IsNullOrWhiteSpace(ServerError)
                    ? "Ván đấu đã kết thúc."
                    : ServerError;
            }

            if (!HasOpponent)
            {
                return "Đang chờ người chơi khác vào phòng...";
            }

            return IsMyTurn
                ? "Lượt của bạn"
                : "Đang chờ đối thủ đi...";
        }
    }

    public void SetDispatcher(Action<Action> dispatcher)
    {
        _dispatchToUI = dispatcher;
    }

    public async Task SendChatAsync()
    {
        string cleanMessage = ChatInputText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(cleanMessage) || cleanMessage.Length > 200 || string.IsNullOrEmpty(RoomId))
        {
            return;
        }

        try
        {
            await _gameClient.SendChatAsync(cleanMessage);
            ChatInputText = string.Empty;
        }
        catch (Exception)
        {
            // Bỏ qua lỗi mạng ngắn hạn khi gửi chat.
        }
    }

    public async Task MakeMoveAsync(int row, int column)
    {
        if (IsGameEnded)
        {
            return;
        }

        try
        {
            await _gameClient.MakeMoveAsync(new BoardPosition(row, column), CancellationToken.None);
        }
        catch (Exception)
        {
            // Bảo vệ UI nếu kết nối mất đúng lúc bấm ô.
        }
    }

    public async Task SendResignAsync()
    {
        if (!CanUseMatchActions)
        {
            return;
        }

        try
        {
            await _gameClient.SendResignAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ServerError = $"Không thể đầu hàng: {ex.Message}";
        }
    }

    public async Task SendDrawOfferAsync()
    {
        if (!CanUseMatchActions)
        {
            return;
        }

        try
        {
            await _gameClient.SendDrawOfferAsync(CancellationToken.None);
            AddSystemMessage("Bạn đã gửi lời xin hòa.");
        }
        catch (Exception ex)
        {
            ServerError = $"Không thể xin hòa: {ex.Message}";
        }
    }

    public async Task SendDrawResponseAsync(bool accepted)
    {
        if (string.IsNullOrWhiteSpace(RoomId))
        {
            return;
        }

        try
        {
            await _gameClient.SendDrawResponseAsync(accepted, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ServerError = $"Không thể phản hồi hòa: {ex.Message}";
        }
    }

    public async Task SendRematchRequestAsync()
    {
        if (!CanRequestRematch)
        {
            return;
        }

        HasRequestedRematch = true;
        HasPendingRematchRequest = false;
        ConnectionStatus = "Đang chờ đối thủ xác nhận...";

        try
        {
            await _gameClient.SendRematchRequestAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            HasRequestedRematch = false;
            ServerError = $"Không thể gửi yêu cầu chơi lại: {ex.Message}";
        }
    }

    public async Task LeaveRoomAsync()
    {
        if (string.IsNullOrWhiteSpace(RoomId))
        {
            return;
        }

        try
        {
            await _gameClient.LeaveRoomAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ServerError = $"Không thể rời phòng: {ex.Message}";
        }
    }

    private void SafeExecuteOnUI(Action action)
    {
        if (_dispatchToUI is not null)
        {
            _dispatchToUI(action);
        }
        else if (_syncContext is not null)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    private void GameClient_GameStateUpdated(object? sender, GameViewState state)
    {
        SafeExecuteOnUI(() => ApplyState(state));
    }

    private void ApplyState(GameViewState state)
    {
        BoardPosition? detectedLastMove = null;
        var changedMoveCount = 0;
        bool startsNewGame =
            state.ConnectionStatus != null &&
            (state.ConnectionStatus.StartsWith("Trận đấu mới") ||
             state.ConnectionStatus.Contains("Đã vào phòng"));
        bool rematchTimedOut = IsRematchTimeoutMessage(state.ServerError);

        foreach (var cellState in state.Cells)
        {
            int index = cellState.Row * BoardSize + cellState.Column;
            if (index >= 0 &&
                index < BoardCells.Count &&
                BoardCells[index].Mark != cellState.Mark &&
                !string.IsNullOrEmpty(cellState.Mark))
            {
                detectedLastMove = new BoardPosition(cellState.Row, cellState.Column);
                changedMoveCount++;
            }
        }

        RoomId = state.RoomId;
        _playerId = state.PlayerId;
        PlayerName = state.PlayerName;
        HasOpponent = state.HasOpponent;
        OpponentName = HasOpponent
            ? string.IsNullOrWhiteSpace(state.OpponentName) ? "Đối thủ" : state.OpponentName
            : "Đang chờ...";
        PlayerSymbol = state.PlayerSymbol;
        CurrentTurnSymbol = state.CurrentTurnSymbol;
        ServerError = startsNewGame ? string.Empty : state.ServerError;
        MyScore = state.MyScore;
        OpponentScore = state.OpponentScore;

        if (startsNewGame)
        {
            ConnectionStatus = state.ConnectionStatus ?? ConnectionStatus;
            HasPendingRematchRequest = false;
            HasRequestedRematch = false;
            IsGameEnded = false;

            foreach (var cell in BoardCells)
            {
                cell.Mark = string.Empty;
                cell.IsWinningCell = false;
                cell.IsLastMove = false;
                cell.IsInteractionEnabled = HasOpponent;
            }
        }
        else
        {
            if (_connectionStatus == "Đang chờ đối thủ xác nhận...")
            {
                if (!string.IsNullOrEmpty(state.ConnectionStatus) &&
                    (state.ConnectionStatus.StartsWith("Trận đấu mới") ||
                     state.ConnectionStatus == "Đối thủ muốn chơi lại!" ||
                     rematchTimedOut))
                {
                    ConnectionStatus = state.ConnectionStatus;
                }
            }
            else
            {
                ConnectionStatus = state.ConnectionStatus ?? ConnectionStatus;
            }

            IsGameEnded = state.ConnectionStatus == "Trò chơi kết thúc" || ServerError == "Ván đấu đã kết thúc.";
            if (state.ConnectionStatus == "Trò chơi kết thúc")
            {
                HasPendingRematchRequest = false;
                HasRequestedRematch = false;
            }

            if (rematchTimedOut)
            {
                HasPendingRematchRequest = false;
                HasRequestedRematch = false;
            }
        }

        foreach (var cellState in state.Cells)
        {
            int index = cellState.Row * BoardSize + cellState.Column;
            if (index >= 0 && index < BoardCells.Count)
            {
                BoardCells[index].Mark = cellState.Mark;
            }
        }

        LastMovePosition = changedMoveCount == 1 ? detectedLastMove : null;
        foreach (var cell in BoardCells)
        {
            cell.IsLastMove =
                LastMovePosition is { } lastMove &&
                cell.Row == lastMove.Row &&
                cell.Column == lastMove.Column;
        }

        if (IsGameEnded || _connectionStatus == "Đang chờ đối thủ xác nhận...")
        {
            foreach (var cell in BoardCells)
            {
                cell.IsInteractionEnabled = false;
            }

            if (_gameClient is SocketGameClientService socketService)
            {
                var targetCells = socketService.WinningCells;
                if (targetCells.Count > 0)
                {
                    foreach (var target in targetCells)
                    {
                        int index = target.Row * BoardSize + target.Col;
                        if (index >= 0 && index < BoardCells.Count)
                        {
                            BoardCells[index].IsWinningCell = true;
                        }
                    }
                }
            }
        }
        else
        {
            foreach (var cell in BoardCells)
            {
                cell.IsInteractionEnabled = HasOpponent;
            }
        }

        OnPropertyChanged(nameof(IsChatInputEnabled));
        OnPropertyChanged(nameof(IsSendButtonEnabled));
        OnPropertyChanged(nameof(HasOpponent));
        OnPropertyChanged(nameof(IsMyTurn));
        OnPropertyChanged(nameof(IsOpponentTurn));
        OnPropertyChanged(nameof(MyHeaderOpacity));
        OnPropertyChanged(nameof(OpponentHeaderOpacity));
        OnPropertyChanged(nameof(MyTurnLabel));
        OnPropertyChanged(nameof(OpponentTurnLabel));
        OnPropertyChanged(nameof(TurnMessage));
        OnPropertyChanged(nameof(LastMovePosition));
        OnPropertyChanged(nameof(CanUseMatchActions));
        OnPropertyChanged(nameof(CanRequestRematch));
        OnPropertyChanged(nameof(RematchHint));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void GameClient_ChatReceived(object? sender, CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload payload)
    {
        SafeExecuteOnUI(() => AddChatMessageToUI(payload));
    }

    private void AddChatMessageToUI(CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload payload)
    {
        ChatMessages.Add(new ChatMessageViewModel(
            payload.SenderName,
            payload.Message,
            payload.Timestamp,
            payload.SenderPlayerId,
            _playerId));

        if (IsRematchRequestMessage(payload) && IsGameEnded && HasOpponent)
        {
            HasPendingRematchRequest = true;
            HasRequestedRematch = false;
            ConnectionStatus = "Đối thủ muốn chơi lại!";
            OnPropertyChanged(nameof(CanRequestRematch));
            OnPropertyChanged(nameof(RematchHint));
        }
    }

    private static bool IsRematchRequestMessage(CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload payload)
    {
        return payload.SenderName.StartsWith("Hệ thống", StringComparison.OrdinalIgnoreCase) &&
            payload.Message.Contains("muốn chơi lại", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRematchTimeoutMessage(string message)
    {
        return message.Contains("Hết thời gian chờ đối thủ đồng ý chơi lại", StringComparison.OrdinalIgnoreCase);
    }

    private void AddSystemMessage(string message)
    {
        ChatMessages.Add(new ChatMessageViewModel(
            "Hệ thống",
            message,
            DateTime.Now,
            null,
            _playerId));
    }

    private void GameClient_DrawOfferReceived(object? sender, DrawOfferReceivedEventArgs e)
    {
        SafeExecuteOnUI(() =>
        {
            AddSystemMessage($"{e.SenderName} muốn hòa.");
            DrawOfferReceived?.Invoke(this, e);
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class ChatMessageViewModel
    {
        public ChatMessageViewModel(
            string senderName,
            string message,
            DateTime timestamp,
            string? senderPlayerId,
            string currentPlayerId)
        {
            SenderName = senderName;
            Message = message;
            Timestamp = timestamp;
            SenderPlayerId = senderPlayerId;
            IsSystemMessage =
                string.IsNullOrWhiteSpace(senderPlayerId) ||
                senderName.Equals("Hệ thống", StringComparison.OrdinalIgnoreCase) ||
                senderName.StartsWith("Hệ thống", StringComparison.OrdinalIgnoreCase);
            IsOwnMessage =
                !IsSystemMessage &&
                !string.IsNullOrWhiteSpace(currentPlayerId) &&
                string.Equals(senderPlayerId, currentPlayerId, StringComparison.OrdinalIgnoreCase);
        }

        public string? SenderPlayerId { get; }
        public string SenderName { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }
        public bool IsOwnMessage { get; }
        public bool IsSystemMessage { get; }
        public bool IsOpponentMessage => !IsOwnMessage && !IsSystemMessage;
        public string TimeText => Timestamp.ToString("HH:mm");
    }
}

public sealed class BoardCellViewModel : INotifyPropertyChanged
{
    private string _mark = string.Empty;
    private bool _isWinningCell;
    private bool _isInteractionEnabled = true;
    private bool _isLastMove;

    public BoardCellViewModel(int row, int column)
    {
        Row = row;
        Column = column;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int Row { get; }
    public int Column { get; }

    public string Mark
    {
        get => _mark;
        set
        {
            if (_mark == value) return;
            _mark = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mark)));
        }
    }

    public bool IsWinningCell
    {
        get => _isWinningCell;
        set
        {
            if (_isWinningCell == value) return;
            _isWinningCell = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsWinningCell)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WinningCellOverlayOpacity)));
        }
    }

    public bool IsInteractionEnabled
    {
        get => _isInteractionEnabled;
        set
        {
            if (_isInteractionEnabled == value) return;
            _isInteractionEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInteractionEnabled)));
        }
    }

    public bool IsLastMove
    {
        get => _isLastMove;
        set
        {
            if (_isLastMove == value) return;
            _isLastMove = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLastMove)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastMoveIndicatorOpacity)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastMoveHighlightOpacity)));
        }
    }

    public double LastMoveIndicatorOpacity => IsLastMove ? 1.0 : 0.0;

    public double LastMoveHighlightOpacity => IsLastMove ? 1.0 : 0.0;

    public double WinningCellOverlayOpacity => IsWinningCell ? 1.0 : 0.0;
}
