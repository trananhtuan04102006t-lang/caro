using System.Collections.Concurrent;
using CaroNet.Server.Host.Networking;

namespace CaroNet.Server.Host.GameRooms;

// Quản lý danh sách phòng. Giới hạn MaxRooms để tránh tràn tài nguyên.
public sealed class RoomManager
{
    private const int MaxRooms = 100;

    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private readonly ConcurrentDictionary<Guid, string> _sessionRoomMap = new();
    private readonly Func<GameRoom> _roomFactory;
    private readonly object _quickMatchLock = new();
    private string? _waitingQuickMatchRoomId;

    public RoomManager()
        : this(() => new GameRoom())
    {
    }

    public RoomManager(Func<GameRoom> roomFactory)
    {
        _roomFactory = roomFactory;
    }

    public int RoomCount => _rooms.Count;

    public GameRoom? CreateRoom(ClientSession session, string playerName)
    {
        if (IsSessionInRoom(session.Id))
        {
            return null;
        }

        if (_rooms.Count >= MaxRooms)
            return null;

        var room = _roomFactory();

        if (!_rooms.TryAdd(room.RoomId, room))
            return null;

        room.TryAddPlayer(session, playerName);
        _sessionRoomMap[session.Id] = room.RoomId;

        Console.WriteLine(
            $"[ROOM] Created {room.RoomId} by {playerName}");

        return room;
    }

    public (GameRoom? room, Shared.Game.PlayerSymbol? symbol) JoinRoom(
        ClientSession session, string roomId, string playerName)
    {
        if (IsSessionInRoom(session.Id))
        {
            return (null, null);
        }

        if (!_rooms.TryGetValue(roomId, out GameRoom? room))
            return (null, null);

        var symbol = room.TryAddPlayer(session, playerName);
        if (symbol is null)
            return (null, null);

        _sessionRoomMap[session.Id] = roomId;

        Console.WriteLine(
            $"[ROOM] {playerName} joined {roomId} as {symbol}");

        return (room, symbol);
    }

    public (GameRoom? room, Shared.Game.PlayerSymbol? symbol, bool matched) JoinQuickMatch(
        ClientSession session,
        string playerName)
    {
        lock (_quickMatchLock)
        {
            if (IsSessionInRoom(session.Id))
            {
                return (null, null, false);
            }

            if (!string.IsNullOrWhiteSpace(_waitingQuickMatchRoomId) &&
                _rooms.TryGetValue(_waitingQuickMatchRoomId, out GameRoom? waitingRoom) &&
                !waitingRoom.IsFull)
            {
                var symbol = waitingRoom.TryAddPlayer(session, playerName);
                if (symbol is not null)
                {
                    _sessionRoomMap[session.Id] = waitingRoom.RoomId;
                    _waitingQuickMatchRoomId = null;

                    Console.WriteLine(
                        $"[QUICK] {playerName} matched room {waitingRoom.RoomId} as {symbol}");

                    return (waitingRoom, symbol, true);
                }
            }

            _waitingQuickMatchRoomId = null;

            GameRoom? room = CreateRoom(session, playerName);
            if (room is null)
            {
                return (null, null, false);
            }

            _waitingQuickMatchRoomId = room.RoomId;
            Console.WriteLine($"[QUICK] {playerName} waiting in room {room.RoomId}");

            return (room, Shared.Game.PlayerSymbol.X, false);
        }
    }

    public GameRoom? GetRoom(string roomId)
    {
        _rooms.TryGetValue(roomId, out GameRoom? room);
        return room;
    }

    public GameRoom? GetRoomBySession(Guid sessionId)
    {
        if (_sessionRoomMap.TryGetValue(sessionId, out string? roomId))
            return GetRoom(roomId);
        return null;
    }

    public bool IsSessionInRoom(Guid sessionId)
    {
        return _sessionRoomMap.ContainsKey(sessionId);
    }

    // Xử lý ngắt kết nối: xóa khỏi phòng, dọn phòng trống.
    public GameRoom? HandleDisconnect(Guid sessionId)
    {
        if (!_sessionRoomMap.TryRemove(sessionId, out string? roomId))
            return null;

        if (!_rooms.TryGetValue(roomId, out GameRoom? room))
            return null;

        room.RemovePlayer(sessionId);

        if (room.IsEmpty)
        {
            _rooms.TryRemove(roomId, out _);
            Console.WriteLine($"[ROOM] {roomId} removed (empty)");
        }

        lock (_quickMatchLock)
        {
            if (string.Equals(_waitingQuickMatchRoomId, roomId, StringComparison.Ordinal))
            {
                _waitingQuickMatchRoomId = null;
            }
        }

        return room;
    }
}
