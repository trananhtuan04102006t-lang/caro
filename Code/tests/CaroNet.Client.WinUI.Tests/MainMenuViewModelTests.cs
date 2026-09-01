using CaroNet.Client.WinUI.Services;
using CaroNet.Client.WinUI.ViewModels;
using CaroNet.Client.WinUI.Models;
using CaroNet.Shared.Game;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit; // Đảm bảo đã có để nhận diện thuộc tính [Fact]

namespace CaroNet.Client.WinUI.Tests;

public sealed class MainMenuViewModelTests
{
    [Fact]
    public void Constructor_restores_authenticated_session_from_game_client()
    {
        var service = new AuthenticatedGameClientService(
            new AuthSession("user-id", "alice", "Alice"));

        var viewModel = new MainMenuViewModel(service);

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal("Alice", viewModel.PlayerName);
        Assert.Equal("alice", viewModel.Username);
        Assert.Equal("Xin chào, Alice", viewModel.GreetingText);
        Assert.Equal("Đã đăng nhập.", viewModel.AuthStatus);
    }

    [Fact]
    public async Task ConnectAsync_returns_false_and_keeps_error_status_when_service_fails()
    {
        var service = new FailingGameClientService(
            new InvalidOperationException("Không thể kết nối server."));
        var viewModel = new MainMenuViewModel(service)
        {
            PlayerName = "Annie"
        };

        bool connected = await viewModel.ConnectAsync();

        Assert.False(connected);
        Assert.Contains("Không thể kết nối server", viewModel.ConnectionStatus);
    }

    [Fact]
    public async Task LoadBestRecordsAsync_uses_top_records_from_game_client()
    {
        var service = new AuthenticatedGameClientService(
            new AuthSession("user-id", "alice", "Alice"));
        var viewModel = new MainMenuViewModel(service);

        await viewModel.LoadBestRecordsAsync();

        BestRecordItem item = Assert.Single(viewModel.BestRecords);
        Assert.Equal(1, item.Rank);
        Assert.Equal("Alice", item.PlayerName);
        Assert.Equal(2, item.Wins);
        Assert.Equal(1, item.Losses);
        Assert.True(viewModel.HasBestRecords);
    }

#pragma warning disable CS0067
    private sealed class AuthenticatedGameClientService : IGameClientService
    {
        private readonly AuthSession _authSession;

        public AuthenticatedGameClientService(AuthSession authSession)
        {
            _authSession = authSession;
            CurrentAuth = authSession;
        }

        public event EventHandler<CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload>? ChatReceived;

        public event EventHandler<DrawOfferReceivedEventArgs>? DrawOfferReceived;

        public event EventHandler<GameViewState>? GameStateUpdated;

        public AuthSession? CurrentAuth { get; }

        public GameViewState CurrentState { get; } = new(
            string.Empty,
            "Alice",
            "?",
            "X",
            "Đã đăng nhập",
            string.Empty,
            []);

        public Task SendChatAsync(string message) => Task.CompletedTask;

        public Task SendResignAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendDrawOfferAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendDrawResponseAsync(bool accepted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendRematchRequestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LeaveRoomAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ConnectAsync(ConnectionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AuthSession> RegisterAsync(
            string username,
            string password,
            string displayName,
            CancellationToken cancellationToken) => Task.FromResult(_authSession);

        public Task<AuthSession> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken) => Task.FromResult(_authSession);

        public Task<GameViewState> CreateRoomAsync(CancellationToken cancellationToken) => Task.FromResult(CurrentState);

        public Task<GameViewState> JoinRoomAsync(
            string roomId,
            CancellationToken cancellationToken) => Task.FromResult(CurrentState);

        public Task<GameViewState> QuickMatchAsync(CancellationToken cancellationToken) => Task.FromResult(CurrentState);

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
                    PlayerName = "Alice",
                    Wins = 2,
                    Losses = 1,
                    Draws = 0
                }
            ];

            return Task.FromResult(records);
        }

        public Task MakeMoveAsync(BoardPosition position, CancellationToken cancellationToken) => Task.CompletedTask;
    }
#pragma warning restore CS0067

#pragma warning disable CS0067
    private sealed class FailingGameClientService(Exception exception) : IGameClientService
    {
        // Vô hiệu hóa cảnh báo CS0067 dành riêng cho các sự kiện giả lập của Test double
        public event EventHandler<CaroNet.Shared.Protocol.Payloads.ChatReceivedPayload>? ChatReceived;

        public event EventHandler<DrawOfferReceivedEventArgs>? DrawOfferReceived;

        public event EventHandler<GameViewState>? GameStateUpdated;
#pragma warning restore CS0067

        public Task SendChatAsync(string message) => Task.CompletedTask;

        public Task SendResignAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendDrawOfferAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendDrawResponseAsync(bool accepted, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendRematchRequestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LeaveRoomAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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
            throw exception;
        }

        public Task<AuthSession> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task ConnectAsync(
            ConnectionRequest request,
            CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task<GameViewState> CreateRoomAsync(CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task<GameViewState> JoinRoomAsync(
            string roomId,
            CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task<GameViewState> QuickMatchAsync(CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task<IReadOnlyList<MatchSummary>> GetMyHistoryAsync(
            CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task<IReadOnlyList<PlayerRecordSummary>> GetTopRecordsAsync(
            CancellationToken cancellationToken)
        {
            throw exception;
        }

        public Task MakeMoveAsync(
            BoardPosition position,
            CancellationToken cancellationToken)
        {
            throw exception;
        }
    }
}
