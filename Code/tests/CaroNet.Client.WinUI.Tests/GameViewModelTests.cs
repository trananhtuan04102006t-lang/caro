using CaroNet.Client.WinUI.Models;
using CaroNet.Client.WinUI.Services;
using CaroNet.Client.WinUI.ViewModels;
using CaroNet.Shared.Game;
using CaroNet.Shared.Protocol.Payloads;

namespace CaroNet.Client.WinUI.Tests;

public sealed class GameViewModelTests
{
    [Fact]
    public void InitialState_ShowsWaitingTurnMessage()
    {
        var viewModel = new GameViewModel(new FakeGameClientService());

        Assert.False(viewModel.IsMyTurn);
        Assert.Equal("Đang chờ đối thủ...", viewModel.TurnMessage);
    }

    [Fact]
    public void GameStateUpdated_WhenCurrentTurnIsPlayer_ShowsMyTurn()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Đã vào phòng 123456",
            string.Empty,
            [],
            HasOpponent: true));

        Assert.True(viewModel.IsMyTurn);
        Assert.Equal("Lượt của bạn", viewModel.TurnMessage);
    }

    [Fact]
    public void GameStateUpdated_WhenRoomHasNoOpponent_ShowsWaitingAndDisablesTurn()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Đã vào phòng 123456",
            string.Empty,
            [],
            HasOpponent: false));

        Assert.False(viewModel.IsMyTurn);
        Assert.False(viewModel.IsOpponentTurn);
        Assert.Equal("Đang chờ người chơi khác vào phòng...", viewModel.TurnMessage);
        Assert.False(viewModel.CanUseMatchActions);
    }

    [Fact]
    public void GameStateUpdated_WhenCurrentTurnIsOpponent_ShowsOpponentTurn()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "O",
            "Đã vào phòng 123456",
            string.Empty,
            [],
            HasOpponent: true));

        Assert.False(viewModel.IsMyTurn);
        Assert.Equal("Đang chờ đối thủ đi...", viewModel.TurnMessage);
    }

    [Fact]
    public void GameStateUpdated_WithOneNewMark_UpdatesLastMoveIndicator()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());
        var board = CreateEmptyCells();
        board[6 * GameViewModel.BoardSize + 7] = new CellViewState(6, 7, "O");

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Đã vào phòng 123456",
            string.Empty,
            board,
            HasOpponent: true));

        Assert.Equal(new BoardPosition(6, 7), viewModel.LastMovePosition);
        Assert.True(viewModel.BoardCells.Single(cell => cell.Row == 6 && cell.Column == 7).IsLastMove);
        Assert.Equal(1.0, viewModel.BoardCells.Single(cell => cell.Row == 6 && cell.Column == 7).LastMoveIndicatorOpacity);
        Assert.False(viewModel.BoardCells.Single(cell => cell.Row == 0 && cell.Column == 0).IsLastMove);
    }

    [Fact]
    public void GameStateUpdated_WhenNewGameStarts_ClearsLastMoveIndicator()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());
        var board = CreateEmptyCells();
        board[6 * GameViewModel.BoardSize + 7] = new CellViewState(6, 7, "O");

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Đã vào phòng 123456",
            string.Empty,
            board,
            HasOpponent: true));

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Trận đấu mới đã bắt đầu!",
            "Ván đấu đã kết thúc.",
            CreateEmptyCells()));

        Assert.Null(viewModel.LastMovePosition);
        Assert.All(viewModel.BoardCells, cell => Assert.False(cell.IsLastMove));
        Assert.False(viewModel.IsGameEnded);
        Assert.Empty(viewModel.ServerError);
    }

    [Fact]
    public void GameStateUpdated_WhenGameEnded_ShowsFinishedTurnMessage()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Trò chơi kết thúc",
            "Bạn thắng!",
            CreateEmptyCells(),
            HasOpponent: false));

        Assert.True(viewModel.IsGameEnded);
        Assert.Equal("Bạn thắng!", viewModel.TurnMessage);
        Assert.Equal("KẾT THÚC", viewModel.MyTurnLabel);
        Assert.Equal("KẾT THÚC", viewModel.OpponentTurnLabel);
    }

    [Fact]
    public void BoardCellViewModel_ExposesOverlayOpacityForHighlights()
    {
        var cell = new BoardCellViewModel(3, 4);

        Assert.Equal(0.0, cell.LastMoveHighlightOpacity);
        Assert.Equal(0.0, cell.WinningCellOverlayOpacity);

        cell.IsLastMove = true;
        cell.IsWinningCell = true;

        Assert.Equal(1.0, cell.LastMoveIndicatorOpacity);
        Assert.Equal(1.0, cell.LastMoveHighlightOpacity);
        Assert.Equal(1.0, cell.WinningCellOverlayOpacity);
    }

    [Fact]
    public void ChatReceived_ClassifiesOwnOpponentAndSystemMessages()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Đã vào phòng 123456",
            string.Empty,
            [],
            HasOpponent: true,
            PlayerId: "alice-id"));

        service.RaiseChat(new ChatReceivedPayload
        {
            SenderPlayerId = "alice-id",
            SenderName = "Alice",
            Message = "Xin chào",
            Timestamp = new DateTime(2026, 7, 3, 9, 5, 0)
        });

        service.RaiseChat(new ChatReceivedPayload
        {
            SenderPlayerId = "bob-id",
            SenderName = "Bob",
            Message = "Chào bạn",
            Timestamp = new DateTime(2026, 7, 3, 9, 6, 0)
        });

        service.RaiseChat(new ChatReceivedPayload
        {
            SenderName = "Hệ thống",
            Message = "Bob muốn hòa.",
            Timestamp = new DateTime(2026, 7, 3, 9, 7, 0)
        });

        Assert.True(viewModel.ChatMessages[0].IsOwnMessage);
        Assert.False(viewModel.ChatMessages[0].IsSystemMessage);
        Assert.Equal("09:05", viewModel.ChatMessages[0].TimeText);
        Assert.False(viewModel.ChatMessages[1].IsOwnMessage);
        Assert.False(viewModel.ChatMessages[1].IsSystemMessage);
        Assert.True(viewModel.ChatMessages[2].IsSystemMessage);
    }

    [Fact]
    public void ChatReceived_WhenOpponentRequestsRematchAfterGameEnded_EnablesRematchAction()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Trò chơi kết thúc",
            "Bạn thắng!",
            CreateEmptyCells(),
            HasOpponent: true));

        service.RaiseChat(new ChatReceivedPayload
        {
            SenderName = "Hệ thống",
            Message = "Đối thủ muốn chơi lại! Bấm Chơi lại để bắt đầu trận mới.",
            Timestamp = new DateTime(2026, 7, 3, 10, 0, 0)
        });

        Assert.True(viewModel.HasPendingRematchRequest);
        Assert.False(viewModel.HasRequestedRematch);
        Assert.True(viewModel.CanRequestRematch);
        Assert.Equal("Đối thủ muốn chơi lại!", viewModel.ConnectionStatus);
        Assert.Equal("Chấp nhận chơi lại", viewModel.RematchActionText);
        Assert.Contains("Đối thủ muốn chơi lại", viewModel.RematchHint);
    }

    [Fact]
    public async Task SendRematchRequestAsync_WhenGameEnded_SendsRequestAndWaitsForOpponent()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Trò chơi kết thúc",
            "Bạn thắng!",
            CreateEmptyCells(),
            HasOpponent: true));

        await viewModel.SendRematchRequestAsync();

        Assert.Equal(1, service.RematchRequestCount);
        Assert.True(viewModel.HasRequestedRematch);
        Assert.False(viewModel.HasPendingRematchRequest);
        Assert.False(viewModel.CanRequestRematch);
        Assert.Equal("Đang chờ đối thủ xác nhận...", viewModel.ConnectionStatus);
    }

    [Fact]
    public async Task GameStateUpdated_WhenRematchTimesOut_AllowsRequestAgain()
    {
        var service = new FakeGameClientService();
        var viewModel = new GameViewModel(service);
        viewModel.SetDispatcher(action => action());

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Trò chơi kết thúc",
            "Bạn thắng!",
            CreateEmptyCells(),
            HasOpponent: true));

        await viewModel.SendRematchRequestAsync();

        service.RaiseState(new GameViewState(
            "123456",
            "Alice",
            "X",
            "X",
            "Trò chơi kết thúc",
            "Hết thời gian chờ đối thủ đồng ý chơi lại (15s).",
            CreateEmptyCells(),
            HasOpponent: true));

        Assert.False(viewModel.HasRequestedRematch);
        Assert.False(viewModel.HasPendingRematchRequest);
        Assert.True(viewModel.CanRequestRematch);
        Assert.Equal("Trò chơi kết thúc", viewModel.ConnectionStatus);
    }

    private static List<CellViewState> CreateEmptyCells()
    {
        var cells = new List<CellViewState>(GameViewModel.BoardSize * GameViewModel.BoardSize);

        for (var row = 0; row < GameViewModel.BoardSize; row++)
        {
            for (var column = 0; column < GameViewModel.BoardSize; column++)
            {
                cells.Add(new CellViewState(row, column, string.Empty));
            }
        }

        return cells;
    }

    private sealed class FakeGameClientService : IGameClientService
    {
#pragma warning disable CS0067
        public event EventHandler<ChatReceivedPayload>? ChatReceived;

        public event EventHandler<DrawOfferReceivedEventArgs>? DrawOfferReceived;

        public event EventHandler<GameViewState>? GameStateUpdated;
#pragma warning restore CS0067

        public int RematchRequestCount { get; private set; }

        public GameViewState CurrentState { get; } = new(
            string.Empty,
            "Player",
            "?",
            "X",
            "Chưa kết nối server",
            string.Empty,
            []);

        public AuthSession? CurrentAuth => null;

        public Task<AuthSession> RegisterAsync(
            string username,
            string password,
            string displayName,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthSession("user-id", username, displayName));
        }

        public Task<AuthSession> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AuthSession("user-id", username, username));
        }

        public Task ConnectAsync(
            ConnectionRequest request,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<GameViewState> CreateRoomAsync(
            CancellationToken cancellationToken) => Task.FromResult(CurrentState);

        public Task<GameViewState> JoinRoomAsync(
            string roomId,
            CancellationToken cancellationToken) => Task.FromResult(CurrentState);

        public Task<GameViewState> QuickMatchAsync(
            CancellationToken cancellationToken) => Task.FromResult(CurrentState);

        public Task<IReadOnlyList<MatchSummary>> GetMyHistoryAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MatchSummary> matches = [];
            return Task.FromResult(matches);
        }

        public Task<IReadOnlyList<PlayerRecordSummary>> GetTopRecordsAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<PlayerRecordSummary> records = [];
            return Task.FromResult(records);
        }

        public Task MakeMoveAsync(
            BoardPosition position,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendChatAsync(string message) => Task.CompletedTask;

        public Task SendResignAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendDrawOfferAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendDrawResponseAsync(bool accepted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendRematchRequestAsync(CancellationToken cancellationToken = default)
        {
            RematchRequestCount++;
            return Task.CompletedTask;
        }

        public Task LeaveRoomAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RaiseState(GameViewState state)
        {
            GameStateUpdated?.Invoke(this, state);
        }

        public void RaiseChat(ChatReceivedPayload payload)
        {
            ChatReceived?.Invoke(this, payload);
        }
    }
}
