using System.Net;
using System.Net.Sockets;
using CaroNet.Server.Host.GameRooms;
using CaroNet.Server.Host.Networking;
using CaroNet.Server.Host.Services;
using CaroNet.Shared.Game;
using Xunit;

namespace CaroNet.Server.Host.Tests;

public sealed class GameRoomTests
{
    private static ClientSession CreateDummySession()
    {
        // Create a connected socket pair for testing
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(IPAddress.Loopback, port);
        var serverSide = listener.Accept();
        listener.Dispose();
        client.Dispose();

        return new ClientSession(serverSide, new LoggingMessageDispatcher());
    }

    [Fact]
    public void TryAddPlayer_FirstPlayer_AssignsX()
    {
        var room = new GameRoom();
        var session = CreateDummySession();

        var symbol = room.TryAddPlayer(session, "Alice");

        Assert.Equal(PlayerSymbol.X, symbol);
        Assert.Equal("Alice", room.PlayerXName);
        Assert.False(room.IsFull);
    }

    [Fact]
    public void TryAddPlayer_SecondPlayer_AssignsO()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();

        room.TryAddPlayer(s1, "Alice");
        var symbol = room.TryAddPlayer(s2, "Bob");

        Assert.Equal(PlayerSymbol.O, symbol);
        Assert.True(room.IsFull);
    }

    [Fact]
    public void TryAddPlayer_ThirdPlayer_ReturnsNull()
    {
        var room = new GameRoom();
        room.TryAddPlayer(CreateDummySession(), "A");
        room.TryAddPlayer(CreateDummySession(), "B");

        var symbol = room.TryAddPlayer(CreateDummySession(), "C");

        Assert.Null(symbol);
    }

    [Fact]
    public async Task JoinQuickMatch_WhenManySessionsJoinConcurrently_KeepsMaxTwoPlayersPerRoom()
    {
        var roomManager = new RoomManager();
        ClientSession[] sessions = Enumerable.Range(0, 5)
            .Select(_ => CreateDummySession())
            .ToArray();

        var results = await Task.WhenAll(
            sessions.Select((session, index) => Task.Run(
                () => roomManager.JoinQuickMatch(session, $"Player{index}"))));

        Assert.All(results, result => Assert.NotNull(result.room));
        Assert.Equal(3, roomManager.RoomCount);

        var groupedRooms = results
            .GroupBy(result => result.room!.RoomId)
            .Select(group => group.Count())
            .ToList();

        Assert.All(groupedRooms, count => Assert.InRange(count, 1, 2));
    }

    [Fact]
    public void TryMakeMove_ValidMove_Succeeds()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "X");
        room.TryAddPlayer(s2, "O");

        var result = room.TryMakeMove(s1.Id, 7, 7);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void TryMakeMove_WrongTurn_Fails()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "X");
        room.TryAddPlayer(s2, "O");

        // O tries to move first — should fail
        var result = room.TryMakeMove(s2.Id, 7, 7);

        Assert.False(result.IsSuccess);
        Assert.Equal(MoveRejectReason.WrongTurn, result.Reason);
    }

    [Fact]
    public void TryMakeMove_CellOccupied_Fails()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "X");
        room.TryAddPlayer(s2, "O");

        room.TryMakeMove(s1.Id, 7, 7); // X plays

        // O tries same cell
        var result = room.TryMakeMove(s2.Id, 7, 7);

        Assert.False(result.IsSuccess);
        Assert.Equal(MoveRejectReason.CellOccupied, result.Reason);
    }

    [Fact]
    public void RemovePlayer_MakesRoomNotFull()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "X");
        room.TryAddPlayer(s2, "O");
        Assert.True(room.IsFull);

        room.RemovePlayer(s2.Id);

        Assert.False(room.IsFull);
    }

    [Fact]
    public void RemovePlayer_BothRemoved_IsEmpty()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "X");
        room.TryAddPlayer(s2, "O");

        room.RemovePlayer(s1.Id);
        room.RemovePlayer(s2.Id);

        Assert.True(room.IsEmpty);
    }

    [Fact]
    public void BuildBoardPayload_ReturnsCorrectSize()
    {
        var room = new GameRoom();
        var board = room.BuildBoardPayload();

        Assert.Equal(15, board.Length);
        Assert.Equal(15, board[0].Length);
    }

    [Fact]
    public void HandleRematchRequest_WhenBothPlayersAccepted_ResetsGame()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "Alice");
        room.TryAddPlayer(s2, "Bob");

        int[,] moves = {
            {0, 0}, {1, 0},
            {0, 1}, {1, 1},
            {0, 2}, {1, 2},
            {0, 3}, {1, 3},
            {0, 4}
        };

        for (int i = 0; i < moves.GetLength(0); i++)
        {
            var currentSession = room.GameState.CurrentPlayer == PlayerSymbol.X ? s1 : s2;
            room.TryMakeMove(currentSession.Id, moves[i, 0], moves[i, 1]);
        }

        var firstRequest = room.HandleRematchRequest(s1.Id);
        var secondRequest = room.HandleRematchRequest(s2.Id);

        Assert.True(firstRequest.Success);
        Assert.False(firstRequest.BothAccepted);
        Assert.True(secondRequest.Success);
        Assert.True(secondRequest.BothAccepted);
        Assert.Equal(GameStatus.Playing, room.GameState.Status);
        Assert.Equal(0, room.GameState.MoveCount);
    }

    [Fact]
    public void HandleResign_WhenPlayerXResigns_EndsWithOWin()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "Alice");
        room.TryAddPlayer(s2, "Bob");

        var result = room.HandleResign(s1.Id);

        Assert.True(result.Success);
        Assert.Equal(GameStatus.OWon, result.Status);
        Assert.Equal(2, result.ActivePlayers.Count);
    }

    [Fact]
    public void HandleDrawOffer_AndAcceptedResponse_EndsWithDraw()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "Alice");
        room.TryAddPlayer(s2, "Bob");

        var offer = room.HandleDrawOffer(s1.Id);
        var response = room.HandleDrawResponse(s2.Id, accepted: true);

        Assert.True(offer.Success);
        Assert.Equal(s2.Id, offer.TargetPlayer?.Id);
        Assert.True(response.Success);
        Assert.True(response.GameEnded);
        Assert.Equal(GameStatus.Draw, room.GameState.Status);
    }

    [Fact]
    public void HandleDrawResponse_WhenDeclined_ClearsPendingOfferWithoutEndingGame()
    {
        var room = new GameRoom();
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "Alice");
        room.TryAddPlayer(s2, "Bob");

        room.HandleDrawOffer(s1.Id);
        var response = room.HandleDrawResponse(s2.Id, accepted: false);

        Assert.True(response.Success);
        Assert.False(response.GameEnded);
        Assert.Equal(GameStatus.Playing, room.GameState.Status);
        Assert.Null(room.PendingDrawOfferPlayerId);
    }

    [Fact]
    public async Task StartTurnTimeout_WhenCurrentPlayerRunsOutOfTime_AwardsOpponentWin()
    {
        var room = new GameRoom(TimeSpan.FromMilliseconds(30));
        var s1 = CreateDummySession();
        var s2 = CreateDummySession();
        room.TryAddPlayer(s1, "Alice");
        room.TryAddPlayer(s2, "Bob");
        var timedOutStatus = new TaskCompletionSource<GameStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        room.StartTurnTimeout((_, status) =>
        {
            timedOutStatus.TrySetResult(status);
            return Task.CompletedTask;
        });

        GameStatus status = await timedOutStatus.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(GameStatus.OWon, status);
        Assert.Equal(GameStatus.OWon, room.GameState.Status);
    }
}

public sealed class RoomManagerTests
{
    private static ClientSession CreateDummySession()
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(IPAddress.Loopback, port);
        var serverSide = listener.Accept();
        listener.Dispose();
        client.Dispose();

        return new ClientSession(serverSide, new LoggingMessageDispatcher());
    }

    [Fact]
    public void CreateRoom_ReturnsRoom()
    {
        var manager = new RoomManager();
        var session = CreateDummySession();

        var room = manager.CreateRoom(session, "Alice");

        Assert.NotNull(room);
        Assert.Equal(1, manager.RoomCount);
    }

    [Fact]
    public void JoinRoom_ValidRoom_Succeeds()
    {
        var manager = new RoomManager();
        var s1 = CreateDummySession();
        var room = manager.CreateRoom(s1, "Alice")!;

        var s2 = CreateDummySession();
        var (joinedRoom, symbol) = manager.JoinRoom(s2, room.RoomId, "Bob");

        Assert.NotNull(joinedRoom);
        Assert.Equal(PlayerSymbol.O, symbol);
        Assert.True(joinedRoom.IsFull);
    }

    [Fact]
    public void JoinRoom_InvalidId_ReturnsNull()
    {
        var manager = new RoomManager();

        var (room, symbol) = manager.JoinRoom(CreateDummySession(), "INVALID", "Bob");

        Assert.Null(room);
        Assert.Null(symbol);
    }

    [Fact]
    public void HandleDisconnect_RemovesFromRoom()
    {
        var manager = new RoomManager();
        var s1 = CreateDummySession();
        var room = manager.CreateRoom(s1, "Alice")!;

        var disconnectedRoom = manager.HandleDisconnect(s1.Id);

        Assert.NotNull(disconnectedRoom);
        Assert.True(room.IsEmpty);
    }

    [Fact]
    public void HandleDisconnect_EmptyRoom_RemovesRoom()
    {
        var manager = new RoomManager();
        var s1 = CreateDummySession();
        manager.CreateRoom(s1, "Alice");
        Assert.Equal(1, manager.RoomCount);

        manager.HandleDisconnect(s1.Id);

        Assert.Equal(0, manager.RoomCount);
    }

    [Fact]
    public void GetRoomBySession_ReturnsCorrectRoom()
    {
        var manager = new RoomManager();
        var session = CreateDummySession();
        var room = manager.CreateRoom(session, "Alice")!;

        var found = manager.GetRoomBySession(session.Id);

        Assert.NotNull(found);
        Assert.Equal(room.RoomId, found.RoomId);
    }

    [Fact]
    public void CreateRoom_SameSessionAlreadyInRoom_ReturnsNullWithoutCreatingGhostRoom()
    {
        var manager = new RoomManager();
        var session = CreateDummySession();
        manager.CreateRoom(session, "Alice");

        var secondRoom = manager.CreateRoom(session, "Alice");

        Assert.Null(secondRoom);
        Assert.Equal(1, manager.RoomCount);
    }

    [Fact]
    public void JoinRoom_SameSessionAlreadyInRoom_ReturnsNullAndKeepsOriginalRoom()
    {
        var manager = new RoomManager();
        var alice = CreateDummySession();
        var bob = CreateDummySession();
        var firstRoom = manager.CreateRoom(alice, "Alice")!;
        var secondRoom = manager.CreateRoom(bob, "Bob")!;

        var (joinedRoom, symbol) = manager.JoinRoom(alice, secondRoom.RoomId, "Alice");

        Assert.Null(joinedRoom);
        Assert.Null(symbol);
        Assert.Equal(firstRoom.RoomId, manager.GetRoomBySession(alice.Id)?.RoomId);
        Assert.False(secondRoom.IsFull);
    }
}
