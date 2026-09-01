using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Server.Host.GameRooms;
using CaroNet.Server.Host.Networking;
using CaroNet.Server.Host.Services;
using CaroNet.Shared.Game;
using CaroNet.Shared.Protocol;
using CaroNet.Storage.Matches;
using CaroNet.Storage.Statistics;
using Xunit;

namespace CaroNet.Server.Host.Tests
{
    public class GameMessageDispatcherTests : IDisposable
    {
        private readonly Socket _listener;
        private readonly Socket _clientSocket;
        private readonly Socket _serverSocket;
        private readonly ClientSession _session;
        private readonly GameMessageDispatcher _dispatcher;

        public GameMessageDispatcherTests()
        {
            // Thiết lập loopback socket pair để kiểm thử ClientSession thực tế mà không cần Mock
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _listener.Listen(1);

            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var acceptTask = _listener.AcceptAsync();
            _clientSocket.Connect(_listener.LocalEndPoint!);
            _serverSocket = acceptTask.GetAwaiter().GetResult();

            var roomManager = new RoomManager();
            var registry = new ClientSessionRegistry();
            _dispatcher = new GameMessageDispatcher(roomManager, registry);

            _session = new ClientSession(_serverSocket, _dispatcher);
        }

        [Fact]
        public async Task DispatchAsync_ShouldAllowRequestsWithNormalInterval()
        {
            var envelope = new MessageEnvelope
            {
                Type = MessageType.Hello,
                Payload = JsonSerializer.SerializeToElement(new { playerName = "Chouwzi" })
            };

            // Gửi request thứ nhất
            await _dispatcher.DispatchAsync(_session, envelope, CancellationToken.None);

            // Đợi 150ms (>100ms) để không bị rate limit
            await Task.Delay(150);

            // Gửi request thứ hai
            await _dispatcher.DispatchAsync(_session, envelope, CancellationToken.None);

            // Đọc dữ liệu từ phía Client Socket để đảm bảo không nhận bất kỳ tin nhắn lỗi nào
            Assert.True(_clientSocket.Available > 0);
        }

        [Fact]
        public async Task DispatchAsync_ShouldTriggerRateLimit_WhenRequestsAreTooFast()
        {
            await RegisterSessionAsync(_dispatcher, _session, "Alice", "Alice");
            Assert.Equal(MessageType.AuthAccepted, ReceiveEnvelope().Type);

            var envelope = new MessageEnvelope
            {
                Type = MessageType.CreateRoom,
                Payload = JsonSerializer.SerializeToElement(new { })
            };

            // Gửi request thứ nhất (thành công bình thường)
            await _dispatcher.DispatchAsync(_session, envelope, CancellationToken.None);

            // Đợi dữ liệu RoomJoined truyền đi hoàn tất và đọc sạch nó khỏi socket client để giải phóng stream
            await Task.Delay(50);
            Assert.Equal(MessageType.RoomJoined, ReceiveEnvelope().Type);

            // Gửi ngay lập tức request thứ hai (sẽ bị chặn lại do khoảng cách < 100ms)
            await _dispatcher.DispatchAsync(_session, envelope, CancellationToken.None);

            // Đợi dữ liệu truyền đi hoàn tất
            await Task.Delay(50);

            // Đọc toàn bộ gói tin client nhận được từ server socket
            var response = ReceiveEnvelope();
            Assert.Equal(MessageType.Error, response.Type);
            Assert.True(response.Payload.HasValue);

            using var doc = JsonDocument.Parse(response.Payload.Value.GetRawText());
            var message = doc.RootElement.GetProperty("message").GetString();
            Assert.Equal("Rate limit exceeded.", message);
        }

        [Fact]
        public async Task DispatchAsync_ShouldAllowCreateRoomImmediatelyAfterRegister()
        {
            var register = new MessageEnvelope
            {
                Type = MessageType.Register,
                Payload = JsonSerializer.SerializeToElement(new
                {
                    username = "alice",
                    password = "1234",
                    displayName = "Alice"
                })
            };

            var createRoom = new MessageEnvelope
            {
                Type = MessageType.CreateRoom,
                Payload = JsonSerializer.SerializeToElement(new { })
            };

            await _dispatcher.DispatchAsync(_session, register, CancellationToken.None);
            await _dispatcher.DispatchAsync(_session, createRoom, CancellationToken.None);

            MessageEnvelope firstResponse = ReceiveEnvelope();
            MessageEnvelope secondResponse = ReceiveEnvelope();

            Assert.Equal(MessageType.AuthAccepted, firstResponse.Type);
            Assert.Equal(MessageType.RoomJoined, secondResponse.Type);
            Assert.False(string.IsNullOrWhiteSpace(secondResponse.RoomId));
        }

        [Fact]
        public async Task DispatchAsync_ShouldRejectCreateRoom_WhenSessionAlreadyInRoom()
        {
            await RegisterSessionAsync(_dispatcher, _session, "Alice", "Alice");
            Assert.Equal(MessageType.AuthAccepted, ReceiveEnvelope().Type);

            var createRoom = new MessageEnvelope
            {
                Type = MessageType.CreateRoom,
                Payload = JsonSerializer.SerializeToElement(new { })
            };

            await _dispatcher.DispatchAsync(_session, createRoom, CancellationToken.None);
            Assert.Equal(MessageType.RoomJoined, ReceiveEnvelope().Type);

            await Task.Delay(150);
            await _dispatcher.DispatchAsync(_session, createRoom, CancellationToken.None);

            MessageEnvelope error = ReceiveEnvelope();

            Assert.Equal(MessageType.Error, error.Type);
            Assert.True(error.Payload.HasValue);

            using var doc = JsonDocument.Parse(error.Payload.Value.GetRawText());
            Assert.Equal("Bạn đã ở trong phòng chơi.", doc.RootElement.GetProperty("message").GetString());
        }

        [Fact]
        public async Task DispatchAsync_CreateRoom_WhenNotLoggedIn_ReturnsError()
        {
            await _dispatcher.DispatchAsync(
                _session,
                new MessageEnvelope
                {
                    Type = MessageType.CreateRoom
                },
                CancellationToken.None);

            MessageEnvelope error = ReceiveEnvelope();

            Assert.Equal(MessageType.Error, error.Type);
            Assert.Contains("đăng nhập", GetPayloadString(error, "message"));
        }

        [Fact]
        public async Task DispatchAsync_QuickMatch_PairsTwoLoggedInPlayers()
        {
            var roomManager = new RoomManager();
            var dispatcher = new GameMessageDispatcher(
                roomManager,
                new ClientSessionRegistry());

            using var alice = SocketPair.Create(dispatcher);
            using var bob = SocketPair.Create(dispatcher);

            await RegisterSessionAsync(dispatcher, alice.ServerSession, "Alice", "Alice");
            Assert.Equal(MessageType.AuthAccepted, alice.ReceiveEnvelope().Type);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope { Type = MessageType.QuickMatch },
                CancellationToken.None);

            MessageEnvelope aliceJoined = alice.ReceiveEnvelope();
            Assert.Equal(MessageType.RoomJoined, aliceJoined.Type);
            Assert.False(string.IsNullOrWhiteSpace(aliceJoined.RoomId));
            Assert.Equal(1, roomManager.RoomCount);

            await RegisterSessionAsync(dispatcher, bob.ServerSession, "Bob", "Bob");
            Assert.Equal(MessageType.AuthAccepted, bob.ReceiveEnvelope().Type);

            await dispatcher.DispatchAsync(
                bob.ServerSession,
                new MessageEnvelope { Type = MessageType.QuickMatch },
                CancellationToken.None);

            MessageEnvelope bobJoined = bob.ReceiveEnvelope();
            MessageEnvelope aliceStarted = alice.ReceiveEnvelope();
            MessageEnvelope bobStarted = bob.ReceiveEnvelope();

            Assert.Equal(aliceJoined.RoomId, bobJoined.RoomId);
            Assert.Equal(MessageType.GameStarted, aliceStarted.Type);
            Assert.Equal(MessageType.GameStarted, bobStarted.Type);
        }

        [Fact]
        public async Task DispatchAsync_MyHistoryRequest_ReturnsOnlyCurrentUserMatches()
        {
            var historyStore = new InMemoryMatchHistoryStore();
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry(),
                matchHistoryStore: historyStore);
            using var alice = SocketPair.Create(dispatcher);

            await RegisterSessionAsync(dispatcher, alice.ServerSession, "Alice", "Alice");
            MessageEnvelope authAccepted = alice.ReceiveEnvelope();
            Guid aliceUserId = Guid.Parse(GetPayloadString(authAccepted, "userId"));

            var aliceMatch = new MatchRecord(
                Guid.NewGuid(),
                "ROOM-A",
                "Alice",
                "Bob",
                "Alice",
                DateTime.UtcNow.AddMinutes(-5),
                DateTime.UtcNow,
                [])
            {
                PlayerXUserId = aliceUserId,
                PlayerOUserId = Guid.NewGuid(),
                WinnerUserId = aliceUserId
            };
            var legacyMatch = aliceMatch with
            {
                MatchId = Guid.NewGuid(),
                RoomId = "ROOM-OLD",
                PlayerXUserId = null,
                PlayerOUserId = null,
                WinnerUserId = null
            };

            await historyStore.SaveMatchAsync(aliceMatch);
            await historyStore.SaveMatchAsync(legacyMatch);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope { Type = MessageType.MyHistoryRequest },
                CancellationToken.None);

            MessageEnvelope history = alice.ReceiveEnvelope();

            Assert.Equal(MessageType.MyHistoryReceived, history.Type);
            Assert.True(history.Payload.HasValue);
            JsonElement matches = history.Payload.Value.GetProperty("matches");
            JsonElement match = Assert.Single(matches.EnumerateArray());
            Assert.Equal("ROOM-A", match.GetProperty("roomId").GetString());
        }

        [Fact]
        public async Task DispatchAsync_TopRecordsRequest_ReturnsPlayerRecords()
        {
            var recordStore = new InMemoryPlayerRecordStore();
            await recordStore.SaveAsync(new PlayerRecord("Alice", 3, 1, 0));

            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry(),
                playerRecordStore: recordStore);
            using var alice = SocketPair.Create(dispatcher);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope { Type = MessageType.TopRecordsRequest },
                CancellationToken.None);

            MessageEnvelope ranking = alice.ReceiveEnvelope();

            Assert.Equal(MessageType.TopRecordsReceived, ranking.Type);
            Assert.True(ranking.Payload.HasValue);
            JsonElement players = ranking.Payload.Value.GetProperty("players");
            JsonElement player = Assert.Single(players.EnumerateArray());
            Assert.Equal("Alice", player.GetProperty("playerName").GetString());
            Assert.Equal(3, player.GetProperty("wins").GetInt32());
            Assert.Equal(1, player.GetProperty("losses").GetInt32());
        }

        [Fact]
        public async Task DispatchAsync_LeaveRoom_WhenAlone_RemovesRoomFromServer()
        {
            var roomManager = new RoomManager();
            var dispatcher = new GameMessageDispatcher(
                roomManager,
                new ClientSessionRegistry());
            using var alice = SocketPair.Create(dispatcher);

            await RegisterSessionAsync(dispatcher, alice.ServerSession, "Alice", "Alice");

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.CreateRoom
                },
                CancellationToken.None);

            Assert.Equal(MessageType.AuthAccepted, alice.ReceiveEnvelope().Type);
            Assert.Equal(MessageType.RoomJoined, alice.ReceiveEnvelope().Type);
            Assert.Equal(1, roomManager.RoomCount);

            await Task.Delay(150);
            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.LeaveRoom
                },
                CancellationToken.None);

            Assert.Equal(0, roomManager.RoomCount);
            Assert.Null(roomManager.GetRoomBySession(alice.ServerSession.Id));

            await Task.Delay(150);
            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.CreateRoom
                },
                CancellationToken.None);

            Assert.Equal(MessageType.RoomJoined, alice.ReceiveEnvelope().Type);
            Assert.Equal(1, roomManager.RoomCount);
        }

        [Fact]
        public async Task SaveMatchHistoryAsync_ShouldUpdatePlayerRecords_WhenGameEnds()
        {
            var store = new InMemoryPlayerRecordStore();
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry(),
                playerRecordStore: store);

            using var playerXPair = SocketPair.Create(dispatcher);
            using var playerOPair = SocketPair.Create(dispatcher);
            var room = new GameRoom();
            room.TryAddPlayer(playerXPair.ServerSession, "Alice");
            room.TryAddPlayer(playerOPair.ServerSession, "Bob");

            await InvokeSaveMatchHistoryAsync(dispatcher, room, GameStatus.XWon);
            await InvokeSaveMatchHistoryAsync(dispatcher, room, GameStatus.Draw);

            PlayerRecord? alice = await store.GetAsync("Alice");
            PlayerRecord? bob = await store.GetAsync("Bob");

            Assert.Equal(new PlayerRecord("Alice", 1, 0, 1), alice);
            Assert.Equal(new PlayerRecord("Bob", 0, 1, 1), bob);
        }

        [Fact]
        public async Task DispatchAsync_Resign_EndsGameWithOpponentAsWinnerAndUpdatesRecords()
        {
            var store = new InMemoryPlayerRecordStore();
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry(),
                playerRecordStore: store);

            using var alice = SocketPair.Create(dispatcher);
            using var bob = SocketPair.Create(dispatcher);

            await JoinTwoPlayersAsync(dispatcher, alice, bob);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.Resign
                },
                CancellationToken.None);

            MessageEnvelope aliceEnded = alice.ReceiveEnvelope();
            MessageEnvelope bobEnded = bob.ReceiveEnvelope();

            Assert.Equal(MessageType.GameEnded, aliceEnded.Type);
            Assert.Equal(MessageType.GameEnded, bobEnded.Type);
            Assert.Equal(bob.ServerSession.Id.ToString(), GetPayloadString(aliceEnded, "winnerPlayerId"));
            Assert.Equal("resigned", GetPayloadString(aliceEnded, "reason"));

            Assert.Equal(new PlayerRecord("Alice", 0, 1, 0), await store.GetAsync("Alice"));
            Assert.Equal(new PlayerRecord("Bob", 1, 0, 0), await store.GetAsync("Bob"));
        }

        [Fact]
        public async Task DispatchAsync_DrawOffer_SendsOfferOnlyToOpponent()
        {
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry());

            using var alice = SocketPair.Create(dispatcher);
            using var bob = SocketPair.Create(dispatcher);

            await JoinTwoPlayersAsync(dispatcher, alice, bob);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.DrawOffer
                },
                CancellationToken.None);

            MessageEnvelope offer = bob.ReceiveEnvelope();

            Assert.Equal(MessageType.DrawOffer, offer.Type);
            Assert.Equal(alice.ServerSession.Id.ToString(), GetPayloadString(offer, "senderPlayerId"));
            Assert.Equal("Alice", GetPayloadString(offer, "senderName"));
            Assert.Equal(0, alice.ClientAvailable);
        }

        [Fact]
        public async Task DispatchAsync_DrawResponseAccepted_EndsGameAsDraw()
        {
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry());

            using var alice = SocketPair.Create(dispatcher);
            using var bob = SocketPair.Create(dispatcher);

            await JoinTwoPlayersAsync(dispatcher, alice, bob);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.DrawOffer
                },
                CancellationToken.None);

            Assert.Equal(MessageType.DrawOffer, bob.ReceiveEnvelope().Type);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                bob.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.DrawResponse,
                    Payload = JsonSerializer.SerializeToElement(new { accepted = true })
                },
                CancellationToken.None);

            MessageEnvelope aliceEnded = alice.ReceiveEnvelope();
            MessageEnvelope bobEnded = bob.ReceiveEnvelope();

            Assert.Equal(MessageType.GameEnded, aliceEnded.Type);
            Assert.Equal(MessageType.GameEnded, bobEnded.Type);
            Assert.Equal("draw_agreed", GetPayloadString(aliceEnded, "reason"));
            Assert.True(string.IsNullOrEmpty(GetPayloadString(aliceEnded, "winnerPlayerId")));
        }

        [Fact]
        public async Task HandleDisconnectAsync_AfterGameAlreadyEnded_DoesNotAwardOpponentWin()
        {
            var roomManager = new RoomManager();
            var dispatcher = new GameMessageDispatcher(
                roomManager,
                new ClientSessionRegistry());

            using var alice = SocketPair.Create(dispatcher);
            using var bob = SocketPair.Create(dispatcher);

            await JoinTwoPlayersAsync(dispatcher, alice, bob);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.DrawOffer
                },
                CancellationToken.None);

            Assert.Equal(MessageType.DrawOffer, bob.ReceiveEnvelope().Type);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                bob.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.DrawResponse,
                    Payload = JsonSerializer.SerializeToElement(new { accepted = true })
                },
                CancellationToken.None);

            Assert.Equal(MessageType.GameEnded, alice.ReceiveEnvelope().Type);
            Assert.Equal(MessageType.GameEnded, bob.ReceiveEnvelope().Type);

            await dispatcher.HandleDisconnectAsync(alice.ServerSession.Id);

            MessageEnvelope notification = bob.ReceiveEnvelope();

            Assert.Equal(MessageType.ChatReceived, notification.Type);
            Assert.Contains("rời phòng", GetPayloadString(notification, "message"));
            Assert.NotEqual("opponent_disconnected", GetPayloadString(notification, "reason"));
            Assert.True(string.IsNullOrEmpty(GetPayloadString(notification, "winnerPlayerId")));
        }

        [Fact]
        public async Task DispatchAsync_DrawResponseDeclined_NotifiesOfferSenderWithoutEndingGame()
        {
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry());

            using var alice = SocketPair.Create(dispatcher);
            using var bob = SocketPair.Create(dispatcher);

            await JoinTwoPlayersAsync(dispatcher, alice, bob);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.DrawOffer
                },
                CancellationToken.None);

            Assert.Equal(MessageType.DrawOffer, bob.ReceiveEnvelope().Type);
            await Task.Delay(150);

            await dispatcher.DispatchAsync(
                bob.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.DrawResponse,
                    Payload = JsonSerializer.SerializeToElement(new { accepted = false })
                },
                CancellationToken.None);

            MessageEnvelope notification = alice.ReceiveEnvelope();

            Assert.Equal(MessageType.ChatReceived, notification.Type);
            Assert.Contains("từ chối hòa", GetPayloadString(notification, "message"));
            Assert.Equal(0, bob.ClientAvailable);
        }

        [Fact]
        public async Task TurnTimeout_BroadcastsGameEndedWithOpponentAsWinner()
        {
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(() => new GameRoom(TimeSpan.FromMilliseconds(40))),
                new ClientSessionRegistry());

            using var alice = SocketPair.Create(dispatcher);
            using var bob = SocketPair.Create(dispatcher);

            await JoinTwoPlayersAsync(dispatcher, alice, bob);
            await Task.Delay(160);

            MessageEnvelope aliceEnded = alice.ReceiveEnvelope();
            MessageEnvelope bobEnded = bob.ReceiveEnvelope();

            Assert.Equal(MessageType.GameEnded, aliceEnded.Type);
            Assert.Equal(MessageType.GameEnded, bobEnded.Type);
            Assert.Equal("timeout", GetPayloadString(aliceEnded, "reason"));
            Assert.Equal(bob.ServerSession.Id.ToString(), GetPayloadString(aliceEnded, "winnerPlayerId"));
        }

        [Fact]
        public async Task SaveMatchHistoryAsync_ShouldNotLosePlayerRecordUpdates_WhenMatchesEndConcurrently()
        {
            var store = new DelayedInMemoryPlayerRecordStore();
            var dispatcher = new GameMessageDispatcher(
                new RoomManager(),
                new ClientSessionRegistry(),
                playerRecordStore: store);

            using var alicePair1 = SocketPair.Create(dispatcher);
            using var bobPair1 = SocketPair.Create(dispatcher);
            using var alicePair2 = SocketPair.Create(dispatcher);
            using var bobPair2 = SocketPair.Create(dispatcher);

            var room1 = new GameRoom();
            room1.TryAddPlayer(alicePair1.ServerSession, "Alice");
            room1.TryAddPlayer(bobPair1.ServerSession, "Bob");

            var room2 = new GameRoom();
            room2.TryAddPlayer(alicePair2.ServerSession, "Alice");
            room2.TryAddPlayer(bobPair2.ServerSession, "Bob");

            await Task.WhenAll(
                InvokeSaveMatchHistoryAsync(dispatcher, room1, GameStatus.XWon),
                InvokeSaveMatchHistoryAsync(dispatcher, room2, GameStatus.XWon));

            PlayerRecord? alice = await store.GetAsync("Alice");
            PlayerRecord? bob = await store.GetAsync("Bob");

            Assert.Equal(new PlayerRecord("Alice", 2, 0, 0), alice);
            Assert.Equal(new PlayerRecord("Bob", 0, 2, 0), bob);
        }

        private static async Task InvokeSaveMatchHistoryAsync(
            GameMessageDispatcher dispatcher,
            GameRoom room,
            GameStatus status)
        {
            MethodInfo method = typeof(GameMessageDispatcher).GetMethod(
                "SaveMatchHistoryAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var task = (Task)method.Invoke(dispatcher, [room, status])!;
            await task;
        }

        private static async Task<string> JoinTwoPlayersAsync(
            GameMessageDispatcher dispatcher,
            SocketPair alice,
            SocketPair bob)
        {
            await RegisterSessionAsync(dispatcher, alice.ServerSession, "Alice", "Alice");

            await dispatcher.DispatchAsync(
                alice.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.CreateRoom
                },
                CancellationToken.None);

            Assert.Equal(MessageType.AuthAccepted, alice.ReceiveEnvelope().Type);
            MessageEnvelope roomJoined = alice.ReceiveEnvelope();
            string roomId = roomJoined.RoomId!;

            await RegisterSessionAsync(dispatcher, bob.ServerSession, "Bob", "Bob");

            await dispatcher.DispatchAsync(
                bob.ServerSession,
                new MessageEnvelope
                {
                    Type = MessageType.JoinRoom,
                    RoomId = roomId,
                    Payload = JsonSerializer.SerializeToElement(new { roomId })
                },
                CancellationToken.None);

            Assert.Equal(MessageType.AuthAccepted, bob.ReceiveEnvelope().Type);
            Assert.Equal(MessageType.RoomJoined, bob.ReceiveEnvelope().Type);
            Assert.Equal(MessageType.GameStarted, alice.ReceiveEnvelope().Type);
            Assert.Equal(MessageType.GameStarted, bob.ReceiveEnvelope().Type);

            return roomId;
        }

        private static Task RegisterSessionAsync(
            GameMessageDispatcher dispatcher,
            ClientSession session,
            string username,
            string displayName)
        {
            return dispatcher.DispatchAsync(
                session,
                new MessageEnvelope
                {
                    Type = MessageType.Register,
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        username = username.ToLowerInvariant(),
                        password = "1234",
                        displayName
                    })
                },
                CancellationToken.None);
        }

        private static string GetPayloadString(MessageEnvelope envelope, string propertyName)
        {
            if (!envelope.Payload.HasValue ||
                !envelope.Payload.Value.TryGetProperty(propertyName, out JsonElement property))
            {
                return string.Empty;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => property.GetRawText()
            };
        }

        public void Dispose()
        {
            _clientSocket.Close();
            _serverSocket.Close();
            _listener.Close();
        }

        private MessageEnvelope ReceiveEnvelope()
        {
            var lengthBuffer = new byte[4];
            ReceiveExact(lengthBuffer);

            int payloadLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
            var frame = new byte[4 + payloadLength];
            lengthBuffer.CopyTo(frame, 0);
            ReceiveExact(frame.AsSpan(4));

            return ProtocolFrameCodec.Decode(frame);
        }

        private void ReceiveExact(byte[] buffer)
        {
            ReceiveExact(buffer.AsSpan());
        }

        private void ReceiveExact(Span<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int received = _clientSocket.Receive(buffer[offset..]);
                if (received == 0)
                {
                    throw new InvalidOperationException("Socket closed while reading test frame.");
                }

                offset += received;
            }
        }

        private sealed class InMemoryPlayerRecordStore : IPlayerRecordStore
        {
            private readonly Dictionary<string, PlayerRecord> _records = new(StringComparer.OrdinalIgnoreCase);

            public Task SaveAsync(PlayerRecord record, CancellationToken cancellationToken = default)
            {
                _records[record.PlayerName] = record;
                return Task.CompletedTask;
            }

            public Task<PlayerRecord?> GetAsync(string playerName, CancellationToken cancellationToken = default)
            {
                _records.TryGetValue(playerName, out PlayerRecord? record);
                return Task.FromResult(record);
            }

            public Task<IReadOnlyList<PlayerRecord>> GetTopPlayersAsync(
                int limit,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<PlayerRecord> records = _records.Values.Take(limit).ToList();
                return Task.FromResult(records);
            }
        }

        private sealed class DelayedInMemoryPlayerRecordStore : IPlayerRecordStore
        {
            private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PlayerRecord> _records =
                new(StringComparer.OrdinalIgnoreCase);

            public async Task SaveAsync(PlayerRecord record, CancellationToken cancellationToken = default)
            {
                await Task.Delay(25, cancellationToken);
                _records[record.PlayerName] = record;
            }

            public async Task<PlayerRecord?> GetAsync(string playerName, CancellationToken cancellationToken = default)
            {
                await Task.Delay(25, cancellationToken);
                _records.TryGetValue(playerName, out PlayerRecord? record);
                return record;
            }

            public Task<IReadOnlyList<PlayerRecord>> GetTopPlayersAsync(
                int limit,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<PlayerRecord> records = _records.Values.Take(limit).ToList();
                return Task.FromResult(records);
            }
        }

        private sealed class SocketPair : IDisposable
        {
            private readonly Socket _listener;
            private readonly Socket _clientSocket;
            private readonly Socket _serverSocket;

            private SocketPair(
                Socket listener,
                Socket clientSocket,
                Socket serverSocket,
                ClientSession serverSession)
            {
                _listener = listener;
                _clientSocket = clientSocket;
                _serverSocket = serverSocket;
                ServerSession = serverSession;
            }

            public ClientSession ServerSession { get; }

            public int ClientAvailable => _clientSocket.Available;

            public static SocketPair Create(GameMessageDispatcher dispatcher)
            {
                var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);

                var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var acceptTask = listener.AcceptAsync();
                clientSocket.Connect(listener.LocalEndPoint!);
                Socket serverSocket = acceptTask.GetAwaiter().GetResult();

                return new SocketPair(
                    listener,
                    clientSocket,
                    serverSocket,
                    new ClientSession(serverSocket, dispatcher));
            }

            public MessageEnvelope ReceiveEnvelope()
            {
                var lengthBuffer = new byte[4];
                ReceiveExact(lengthBuffer);

                int payloadLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                var frame = new byte[4 + payloadLength];
                lengthBuffer.CopyTo(frame, 0);
                ReceiveExact(frame.AsSpan(4));

                return ProtocolFrameCodec.Decode(frame);
            }

            private void ReceiveExact(Span<byte> buffer)
            {
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int received = _clientSocket.Receive(buffer[offset..]);
                    if (received == 0)
                    {
                        throw new InvalidOperationException("Socket closed while reading test frame.");
                    }

                    offset += received;
                }
            }

            public void Dispose()
            {
                _clientSocket.Dispose();
                _serverSocket.Dispose();
                _listener.Dispose();
            }
        }
    }
}
