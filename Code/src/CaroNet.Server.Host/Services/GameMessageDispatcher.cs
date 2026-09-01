using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CaroNet.Server.Host.GameRooms;
using CaroNet.Server.Host.Networking;
using CaroNet.Shared.Game;
using CaroNet.Shared.Protocol;
using CaroNet.Shared.Protocol.Payloads;
using CaroNet.Storage.Matches;
using CaroNet.Storage.Statistics;
using CaroNet.Storage.Users;

namespace CaroNet.Server.Host.Services;

// Xử lý message từ client: Hello, CreateRoom, JoinRoom, MakeMove.
public sealed class GameMessageDispatcher : IMessageDispatcher
{
    private readonly RoomManager _roomManager;
    private readonly ClientSessionRegistry _registry;
    private readonly IMatchHistoryStore? _matchHistoryStore;
    private readonly IPlayerRecordStore? _playerRecordStore;
    private readonly IUserAccountStore _userAccountStore;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> _playerNames = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, AuthenticatedUser> _authenticatedUsers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _lastRequestTimes = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _playerRecordLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public GameMessageDispatcher(
        RoomManager roomManager,
        ClientSessionRegistry registry,
        IMatchHistoryStore? matchHistoryStore = null,
        IPlayerRecordStore? playerRecordStore = null,
        IUserAccountStore? userAccountStore = null)
    {
        _roomManager = roomManager;
        _registry = registry;
        _matchHistoryStore = matchHistoryStore;
        _playerRecordStore = playerRecordStore;
        _userAccountStore = userAccountStore ?? new InMemoryUserAccountStore();
    }

    public async Task DispatchAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[DISPATCH] Client={session.Id} Type={message.Type}");

        // Bỏ qua handshake/auth để người chơi có thể vào trận ngay sau đăng nhập.
        var now = DateTime.UtcNow;
        bool bypassRateLimit = message.Type is MessageType.Hello or MessageType.Register or MessageType.Login;
        if (!bypassRateLimit &&
            _lastRequestTimes.TryGetValue(session.Id, out var lastTime) &&
            (now - lastTime).TotalMilliseconds < 100)
        {
            await SendErrorAsync(session, "Rate limit exceeded.", cancellationToken);
            return;
        }

        if (!bypassRateLimit)
        {
            _lastRequestTimes[session.Id] = now;
        }

        switch (message.Type)
        {
            case MessageType.Hello:
                await HandleHelloAsync(session, message, cancellationToken);
                break;

            case MessageType.CreateRoom:
                await HandleCreateRoomAsync(session, message, cancellationToken);
                break;

            case MessageType.Register:
                await HandleRegisterAsync(session, message, cancellationToken);
                break;

            case MessageType.Login:
                await HandleLoginAsync(session, message, cancellationToken);
                break;

            case MessageType.QuickMatch:
                await HandleQuickMatchAsync(session, cancellationToken);
                break;

            case MessageType.MyHistoryRequest:
                await HandleMyHistoryRequestAsync(session, cancellationToken);
                break;

            case MessageType.TopRecordsRequest:
                await HandleTopRecordsRequestAsync(session, cancellationToken);
                break;

            case MessageType.JoinRoom:
                await HandleJoinRoomAsync(session, message, cancellationToken);
                break;

            case MessageType.MakeMove:
                await HandleMakeMoveAsync(session, message, cancellationToken);
                break;
            case MessageType.Rematch:
                await HandleRematchAsync(session, message, cancellationToken);
                break;

            case MessageType.Resign:
                await HandleResignAsync(session, cancellationToken);
                break;

            case MessageType.DrawOffer:
                await HandleDrawOfferAsync(session, cancellationToken);
                break;

            case MessageType.DrawResponse:
                await HandleDrawResponseAsync(session, message, cancellationToken);
                break;

            case MessageType.LeaveRoom:
                await HandleLeaveRoomAsync(session, cancellationToken);
                break;

            case MessageType.Chat:
                await HandleChatAsync(session, message, cancellationToken);
                break;

            default:
                Console.WriteLine(
                    $"[DISPATCH] Unhandled message type: {message.Type}");
                break;
        }
    }

    // Xử lý khi client ngắt kết nối.
    public async Task HandleDisconnectAsync(Guid sessionId)
    {
        _playerNames.TryRemove(sessionId, out _);
        _authenticatedUsers.TryRemove(sessionId, out _);
        _lastRequestTimes.TryRemove(sessionId, out _); // Dọn dẹp bộ giới hạn tốc độ (Issue #53)

        GameRoom? room = _roomManager.HandleDisconnect(sessionId);
        if (room is null) return;

        room.StopRematchTimeout();
        room.StopTurnTimeout();

        if (room.GameState.Status != GameStatus.Playing)
        {
            await NotifyOpponentLeftAfterGameEndedAsync(room);
            return;
        }

        // Báo cho người còn lại biết đối thủ đã thoát.
        foreach (var player in room.GetPlayers())
        {
            try
            {
                Console.WriteLine(
                    $"[DISCONNECT] Sending GameEnded to {player.Id}");

                await player.SendAsync(
                    new MessageEnvelope
                    {
                        Type = MessageType.GameEnded,
                        RoomId = room.RoomId,
                        Payload = JsonSerializer.SerializeToElement(new GameEndedPayload
                        {
                            WinnerPlayerId = player.Id.ToString(),
                            Reason = "opponent_disconnected",
                            Board = room.BuildBoardPayload()
                        })
                    },
                    CancellationToken.None);

                Console.WriteLine(
                    $"[DISCONNECT] GameEnded sent to {player.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[DISCONNECT] Failed to notify player {player.Id}: {ex.Message}");
            }
        }
    }

    private static async Task NotifyOpponentLeftAfterGameEndedAsync(GameRoom room)
    {
        foreach (var player in room.GetPlayers())
        {
            try
            {
                await player.SendAsync(
                    new MessageEnvelope
                    {
                        Type = MessageType.ChatReceived,
                        RoomId = room.RoomId,
                        Payload = JsonSerializer.SerializeToElement(new ChatReceivedPayload
                        {
                            SenderName = "Hệ thống",
                            Message = "Đối thủ đã rời phòng sau khi ván đấu kết thúc.",
                            Timestamp = DateTime.Now
                        })
                    },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[DISCONNECT] Failed to notify ended room player {player.Id}: {ex.Message}");
            }
        }
    }

    private async Task HandleHelloAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        string playerName = "Player";

        if (message.Payload.HasValue)
        {
            try
            {
                var hello = message.Payload.Value.Deserialize<HelloPayload>();
                if (!string.IsNullOrWhiteSpace(hello?.PlayerName))
                    playerName = hello.PlayerName.Trim();
            }
            catch { /* use default name */ }
        }

        playerName = CaroNet.Server.Host.Validation.PlayerNameSanitizer.Sanitize(playerName);

        _playerNames[session.Id] = playerName;

        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.HelloAccepted,
            PlayerId = session.Id.ToString(),
            Payload = JsonSerializer.SerializeToElement(new
            {
                playerName,
                playerId = session.Id.ToString()
            })
        }, cancellationToken);
    }

    private async Task HandleRegisterAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        AuthRequestPayload? payload = DeserializeAuthPayload(message);
        if (payload is null)
        {
            await SendErrorAsync(session, "Dữ liệu đăng ký không hợp lệ.", cancellationToken);
            return;
        }

        try
        {
            UserAccount account = await _userAccountStore.RegisterAsync(
                payload.Username,
                payload.Password,
                payload.DisplayName,
                cancellationToken);

            await AcceptAuthenticatedUserAsync(session, account, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await SendErrorAsync(session, ex.Message, cancellationToken);
        }
    }

    private async Task HandleLoginAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        AuthRequestPayload? payload = DeserializeAuthPayload(message);
        if (payload is null)
        {
            await SendErrorAsync(session, "Dữ liệu đăng nhập không hợp lệ.", cancellationToken);
            return;
        }

        try
        {
            UserAccount? account = await _userAccountStore.LoginAsync(
                payload.Username,
                payload.Password,
                cancellationToken);

            if (account is null)
            {
                await SendErrorAsync(session, "Tên đăng nhập hoặc mật khẩu không đúng.", cancellationToken);
                return;
            }

            await AcceptAuthenticatedUserAsync(session, account, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            await SendErrorAsync(session, ex.Message, cancellationToken);
        }
    }

    private async Task AcceptAuthenticatedUserAsync(
        ClientSession session,
        UserAccount account,
        CancellationToken cancellationToken)
    {
        var authenticatedUser = new AuthenticatedUser(
            account.UserId,
            account.Username,
            account.DisplayName);

        _authenticatedUsers[session.Id] = authenticatedUser;
        _playerNames[session.Id] = account.DisplayName;

        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.AuthAccepted,
            PlayerId = session.Id.ToString(),
            Payload = JsonSerializer.SerializeToElement(new AuthAcceptedPayload
            {
                UserId = account.UserId.ToString(),
                Username = account.Username,
                DisplayName = account.DisplayName
            })
        }, cancellationToken);
    }

    private async Task HandleCreateRoomAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out AuthenticatedUser user))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        string playerName = user.DisplayName;

        if (_roomManager.IsSessionInRoom(session.Id))
        {
            await SendErrorAsync(session, "Bạn đã ở trong phòng chơi.", cancellationToken);
            return;
        }

        GameRoom? room = _roomManager.CreateRoom(session, playerName);

        if (room is null)
        {
            await SendErrorAsync(session, "Không thể tạo phòng. Server đã đầy.", cancellationToken);
            return;
        }

        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.RoomJoined,
            RoomId = room.RoomId,
            PlayerId = session.Id.ToString(),
            Payload = JsonSerializer.SerializeToElement(new
            {
                roomId = room.RoomId,
                symbol = "X",
                playerName
            })
        }, cancellationToken);
    }

    private async Task HandleJoinRoomAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out AuthenticatedUser user))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        string? roomId = message.RoomId;

        if (string.IsNullOrWhiteSpace(roomId) && message.Payload.HasValue)
        {
            try
            {
                var joinPayload = message.Payload.Value.Deserialize<JoinRoomPayload>();
                roomId = joinPayload?.RoomId;
            }
            catch { /* invalid payload */ }
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            await SendErrorAsync(session, "Mã phòng không hợp lệ.", cancellationToken);
            return;
        }

        string playerName = user.DisplayName;

        if (_roomManager.IsSessionInRoom(session.Id))
        {
            await SendErrorAsync(session, "Bạn đã ở trong phòng chơi.", cancellationToken);
            return;
        }

        var (room, symbol) = _roomManager.JoinRoom(session, roomId, playerName);

        if (room is null || symbol is null)
        {
            await SendErrorAsync(session, "Phòng không tồn tại hoặc đã đầy.", cancellationToken);
            return;
        }

        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.RoomJoined,
            RoomId = room.RoomId,
            PlayerId = session.Id.ToString(),
            Payload = JsonSerializer.SerializeToElement(new
            {
                roomId = room.RoomId,
                symbol = symbol.Value.ToString(),
                playerName
            })
        }, cancellationToken);

        // Đủ 2 người, bắt đầu ván
        if (room.IsFull)
        {
            await BroadcastGameStartedAsync(room, cancellationToken);
            StartTurnTimeout(room);
        }
    }

    private async Task HandleQuickMatchAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out AuthenticatedUser user))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        if (_roomManager.IsSessionInRoom(session.Id))
        {
            await SendErrorAsync(session, "Bạn đã ở trong phòng chơi.", cancellationToken);
            return;
        }

        var (room, symbol, matched) = _roomManager.JoinQuickMatch(session, user.DisplayName);
        if (room is null || symbol is null)
        {
            await SendErrorAsync(session, "Không thể ghép trận nhanh lúc này.", cancellationToken);
            return;
        }

        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.RoomJoined,
            RoomId = room.RoomId,
            PlayerId = session.Id.ToString(),
            Payload = JsonSerializer.SerializeToElement(new
            {
                roomId = room.RoomId,
                symbol = symbol.Value.ToString(),
                playerName = user.DisplayName,
                quickMatch = true
            })
        }, cancellationToken);

        if (matched && room.IsFull)
        {
            await BroadcastGameStartedAsync(room, cancellationToken);
            StartTurnTimeout(room);
        }
    }

    private async Task HandleMakeMoveAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out _))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        GameRoom? room = _roomManager.GetRoomBySession(session.Id);
        if (room is null)
        {
            await SendErrorAsync(session, "Bạn chưa vào phòng nào.", cancellationToken);
            return;
        }

        if (!room.IsFull)
        {
            await SendErrorAsync(session, "Chờ đối thủ vào phòng.", cancellationToken);
            return;
        }

        int row = 0, col = 0;

        if (message.Payload.HasValue)
        {
            try
            {
                var movePayload = message.Payload.Value.Deserialize<MakeMovePayload>();
                if (movePayload is not null)
                {
                    row = movePayload.Row;
                    col = movePayload.Column;
                }
            }
            catch
            {
                await SendErrorAsync(session, "Dữ liệu nước đi không hợp lệ.", cancellationToken);
                return;
            }
        }

        MoveResult result = room.TryMakeMove(session.Id, row, col);

        if (!result.IsSuccess)
        {
            string reason = result.Reason switch
            {
                MoveRejectReason.CellOccupied => "Ô này đã được đánh.",
                MoveRejectReason.WrongTurn => "Chưa đến lượt của bạn.",
                MoveRejectReason.OutOfBounds => "Tọa độ ngoài bàn cờ.",
                MoveRejectReason.GameEnded => "Trò chơi đã kết thúc.",
                _ => "Nước đi không hợp lệ."
            };

            await session.SendAsync(new MessageEnvelope
            {
                Type = MessageType.MoveRejected,
                RoomId = room.RoomId,
                Payload = JsonSerializer.SerializeToElement(new { reason })
            }, cancellationToken);

            return;
        }

        await BroadcastGameStateAsync(room, cancellationToken);

        if (result.Status != GameStatus.Playing)
        {
            room.StopTurnTimeout();
            // TRUYỀN THÊM row VÀ col VÀO ĐỂ KHÔNG BỊ LỖI THUỘC TÍNH
            await BroadcastGameEndedAsync(room, result.Status, row, col, cancellationToken);
            await SaveMatchHistoryAsync(room, result.Status);
        }
        else
        {
            room.ResetTurnTimeout();
        }
    }

    private async Task HandleChatAsync(
    ClientSession session,
    MessageEnvelope message,
    CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out _))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chat.", cancellationToken);
            return;
        }

        // 1. Kiểm tra xem người chơi đã ở trong Room chưa (Issue: không cho gửi khi chưa vào room)
        GameRoom? room = _roomManager.GetRoomBySession(session.Id);
        if (room is null) return;

        // 2. Kiểm tra dữ liệu gói tin gửi lên
        if (!message.Payload.HasValue) return;

        try
        {
            var chatPayload = message.Payload.Value.Deserialize<ChatPayload>();
            string? processedMessage = chatPayload?.Message?.Trim();

            // Input validation: Reject nếu rỗng hoặc quá 200 ký tự
            if (string.IsNullOrEmpty(processedMessage) || processedMessage.Length > 200)
                return;

            // 3. Lấy tên người gửi đã đăng ký trong hệ thống Server
            string senderName = GetPlayerName(session.Id);

            // 4. Tạo Payload phát sóng chuẩn ChatReceivedPayload
            var broadcastPayload = new ChatReceivedPayload
            {
                SenderPlayerId = session.Id.ToString(),
                SenderName = senderName,
                Message = processedMessage,
                Timestamp = DateTime.Now
            };

            // 5. Đóng gói vào MessageEnvelope phát sóng cho cả room
            var envelope = new MessageEnvelope
            {
                Type = MessageType.ChatReceived,
                RoomId = room.RoomId,
                Payload = JsonSerializer.SerializeToElement(broadcastPayload)
            };

            // 6. Tiến hành Broadcast tới tất cả các Player trong Room (bắt chước logic MakeMove)
            foreach (var player in room.GetPlayers())
            {
                try
                {
                    await player.SendAsync(envelope, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CHAT BROADCAST ERROR] {player.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CHAT ERROR] {session.Id}: {ex.Message}");
        }
    }

    private async Task HandleResignAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out _))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        GameRoom? room = _roomManager.GetRoomBySession(session.Id);
        if (room is null)
        {
            await SendErrorAsync(session, "Bạn chưa vào phòng nào.", cancellationToken);
            return;
        }

        var result = room.HandleResign(session.Id);
        if (!result.Success)
        {
            await SendErrorAsync(session, "Không thể đầu hàng lúc này.", cancellationToken);
            return;
        }

        await BroadcastGameEndedAsync(room, result.Status, "resigned", cancellationToken);
        await SaveMatchHistoryAsync(room, result.Status);
    }

    private async Task HandleDrawOfferAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out _))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        GameRoom? room = _roomManager.GetRoomBySession(session.Id);
        if (room is null)
        {
            await SendErrorAsync(session, "Bạn chưa vào phòng nào.", cancellationToken);
            return;
        }

        var result = room.HandleDrawOffer(session.Id);
        if (!result.Success || result.TargetPlayer is null)
        {
            await SendErrorAsync(session, "Không thể xin hòa lúc này.", cancellationToken);
            return;
        }

        await result.TargetPlayer.SendAsync(new MessageEnvelope
        {
            Type = MessageType.DrawOffer,
            RoomId = room.RoomId,
            Payload = JsonSerializer.SerializeToElement(new
            {
                senderPlayerId = session.Id.ToString(),
                senderName = GetPlayerName(session.Id)
            })
        }, cancellationToken);
    }

    private async Task HandleDrawResponseAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out _))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        GameRoom? room = _roomManager.GetRoomBySession(session.Id);
        if (room is null)
        {
            await SendErrorAsync(session, "Bạn chưa vào phòng nào.", cancellationToken);
            return;
        }

        bool accepted = false;
        if (message.Payload.HasValue)
        {
            try
            {
                accepted = message.Payload.Value.Deserialize<DrawResponsePayload>()?.Accepted ?? false;
            }
            catch
            {
                await SendErrorAsync(session, "Phản hồi hòa không hợp lệ.", cancellationToken);
                return;
            }
        }

        var result = room.HandleDrawResponse(session.Id, accepted);
        if (!result.Success)
        {
            await SendErrorAsync(session, "Không có lời xin hòa hợp lệ.", cancellationToken);
            return;
        }

        if (result.GameEnded)
        {
            await BroadcastGameEndedAsync(room, GameStatus.Draw, "draw_agreed", cancellationToken);
            await SaveMatchHistoryAsync(room, GameStatus.Draw);
            return;
        }

        if (result.OfferSender is not null)
        {
            await result.OfferSender.SendAsync(new MessageEnvelope
            {
                Type = MessageType.ChatReceived,
                RoomId = room.RoomId,
                Payload = JsonSerializer.SerializeToElement(new ChatReceivedPayload
                {
                    SenderName = "Hệ thống",
                    Message = "Đối thủ đã từ chối hòa.",
                    Timestamp = DateTime.Now
                })
            }, cancellationToken);
        }
    }

    private async Task HandleLeaveRoomAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        GameRoom? room = _roomManager.GetRoomBySession(session.Id);
        if (room is null)
        {
            return;
        }

        if (room.IsFull && room.GameState.Status == GameStatus.Playing)
        {
            var result = room.HandleResign(session.Id);
            if (result.Success)
            {
                await BroadcastGameEndedAsync(
                    room,
                    result.Status,
                    "resigned",
                    cancellationToken,
                    excludedPlayerId: session.Id);
                await SaveMatchHistoryAsync(room, result.Status);
            }
        }

        GameRoom? updatedRoom = _roomManager.HandleDisconnect(session.Id);
        if (updatedRoom is null)
        {
            return;
        }

        updatedRoom.StopRematchTimeout();
        updatedRoom.StopTurnTimeout();

        // Rời phòng không đồng nghĩa mất phiên đăng nhập trên socket hiện tại.
        if (updatedRoom.GameState.Status != GameStatus.Playing)
        {
            await NotifyOpponentLeftAfterGameEndedAsync(updatedRoom);
        }
    }

    private async Task BroadcastGameStartedAsync(
        GameRoom room, CancellationToken cancellationToken)
    {
        var gameState = BuildGameStatePayload(room);

        foreach (var player in room.GetPlayers())
        {
            try
            {
                var symbol = room.GetPlayerSymbol(player.Id);
                await player.SendAsync(new MessageEnvelope
                {
                    Type = MessageType.GameStarted,
                    RoomId = room.RoomId,
                    PlayerId = player.Id.ToString(),
                    Payload = JsonSerializer.SerializeToElement(new
                    {
                        roomId = room.RoomId,
                        yourSymbol = symbol?.ToString() ?? "",
                        opponentName = GetOpponentName(room, player.Id),
                        board = gameState.Board,
                        currentTurnPlayerId = gameState.CurrentTurnPlayerId
                    })
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[BROADCAST ERROR] {player.Id}: {ex.Message}");
            }
        }
    }

    private void StartTurnTimeout(GameRoom room)
    {
        room.StartTurnTimeout(async (timedOutRoom, status) =>
        {
            await BroadcastGameEndedAsync(timedOutRoom, status, "timeout", CancellationToken.None);
            await SaveMatchHistoryAsync(timedOutRoom, status);
        });
    }

    private async Task BroadcastGameStateAsync(
        GameRoom room, CancellationToken cancellationToken)
    {
        var payload = BuildGameStatePayload(room);
        var envelope = new MessageEnvelope
        {
            Type = MessageType.GameStateUpdated,
            RoomId = room.RoomId,
            Payload = JsonSerializer.SerializeToElement(payload)
        };

        foreach (var player in room.GetPlayers())
        {
            try
            {
                await player.SendAsync(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[BROADCAST ERROR] {player.Id}: {ex.Message}");
            }
        }
    }

    // THAY ĐỔI: Hàm nhận thêm tham số int lastRow, int lastCol để quét ô thắng trực tiếp
    private async Task BroadcastGameEndedAsync(
         GameRoom room, GameStatus status, int lastRow, int lastCol, CancellationToken cancellationToken)
    {
        string? winnerId = status switch
        {
            GameStatus.XWon => room.PlayerX?.Id.ToString(),
            GameStatus.OWon => room.PlayerO?.Id.ToString(),
            _ => null
        };

        // SỬA TẠI ĐÂY: Tính toán và chuyển đổi danh sách tọa độ sang JSON Payload an toàn
        IReadOnlyList<BoardPosition>? winningCells = null;

        if (status == GameStatus.XWon || status == GameStatus.OWon)
        {
            var rawCells = CaroRuleEngine.GetWinningCells(room.GameState, lastRow, lastCol);
            winningCells = rawCells
                .Select(cell => new BoardPosition(cell.Row, cell.Col))
                .ToList();
        }

        var envelope = new MessageEnvelope
        {
            Type = MessageType.GameEnded,
            RoomId = room.RoomId,
            Payload = JsonSerializer.SerializeToElement(new GameEndedPayload
            {
                WinnerPlayerId = winnerId,
                Reason = null,
                Board = room.BuildBoardPayload(),
                WinningCells = winningCells
            })
        };

        foreach (var player in room.GetPlayers())
        {
            try
            {
                await player.SendAsync(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BROADCAST ERROR] {player.Id}: {ex.Message}");
            }
        }
    }

    private async Task BroadcastGameEndedAsync(
        GameRoom room,
        GameStatus status,
        string reason,
        CancellationToken cancellationToken,
        Guid? excludedPlayerId = null)
    {
        string? winnerId = status switch
        {
            GameStatus.XWon => room.PlayerX?.Id.ToString(),
            GameStatus.OWon => room.PlayerO?.Id.ToString(),
            _ => null
        };

        var envelope = new MessageEnvelope
        {
            Type = MessageType.GameEnded,
            RoomId = room.RoomId,
            Payload = JsonSerializer.SerializeToElement(new GameEndedPayload
            {
                WinnerPlayerId = winnerId,
                Reason = reason,
                Board = room.BuildBoardPayload()
            })
        };

        foreach (var player in room.GetPlayers())
        {
            if (excludedPlayerId.HasValue && player.Id == excludedPlayerId.Value)
            {
                continue;
            }

            try
            {
                await player.SendAsync(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BROADCAST ERROR] {player.Id}: {ex.Message}");
            }
        }
    }

    private GameStatePayload BuildGameStatePayload(GameRoom room)
    {
        string currentTurnId = room.GameState.CurrentPlayer == PlayerSymbol.X
            ? room.PlayerX?.Id.ToString() ?? ""
            : room.PlayerO?.Id.ToString() ?? "";

        return new GameStatePayload
        {
            CurrentTurnPlayerId = currentTurnId,
            Board = room.BuildBoardPayload(),
            IsGameOver = room.GameState.Status != GameStatus.Playing,
            WinnerPlayerId = room.GameState.Status switch
            {
                GameStatus.XWon => room.PlayerX?.Id.ToString(),
                GameStatus.OWon => room.PlayerO?.Id.ToString(),
                _ => null
            }
        };
    }

    private async Task HandleMyHistoryRequestAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out AuthenticatedUser user))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập để xem lịch sử.", cancellationToken);
            return;
        }

        IReadOnlyList<MatchRecord> matches = _matchHistoryStore is null
            ? []
            : await _matchHistoryStore.GetMatchesByUserIdAsync(user.UserId, cancellationToken);

        var payload = new MyHistoryReceivedPayload
        {
            Matches = matches
                .Select(match => new MyHistoryMatchPayload
                {
                    RoomId = match.RoomId,
                    PlayerXName = match.PlayerXName,
                    PlayerOName = match.PlayerOName,
                    WinnerName = match.WinnerName,
                    PlayedAtUtc = match.EndedAtUtc ?? match.StartedAtUtc,
                    MoveCount = match.Moves.Count
                })
                .ToList()
        };

        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.MyHistoryReceived,
            Payload = JsonSerializer.SerializeToElement(payload)
        }, cancellationToken);
    }

    private async Task HandleTopRecordsRequestAsync(
        ClientSession session,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PlayerRecord> records = _playerRecordStore is null
            ? []
            : await _playerRecordStore.GetTopPlayersAsync(10, cancellationToken);

        var payload = new TopRecordsReceivedPayload
        {
            Players = records
                .Select(record => new TopPlayerRecordPayload
                {
                    PlayerName = record.PlayerName,
                    Wins = record.Wins,
                    Losses = record.Losses,
                    Draws = record.Draws
                })
                .ToList()
        };

        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.TopRecordsReceived,
            Payload = JsonSerializer.SerializeToElement(payload)
        }, cancellationToken);
    }

    private async Task SendErrorAsync(
        ClientSession session, string errorMessage,
        CancellationToken cancellationToken)
    {
        await session.SendAsync(new MessageEnvelope
        {
            Type = MessageType.Error,
            Payload = JsonSerializer.SerializeToElement(new
            {
                message = errorMessage
            })
        }, cancellationToken);
    }

    private string GetPlayerName(Guid sessionId)
    {
        if (_authenticatedUsers.TryGetValue(sessionId, out AuthenticatedUser? user))
        {
            return user.DisplayName;
        }

        return _playerNames.TryGetValue(sessionId, out string? name)
            ? name
            : "Player";
    }

    private bool TryGetAuthenticatedUser(
        ClientSession session,
        out AuthenticatedUser user)
    {
        return _authenticatedUsers.TryGetValue(session.Id, out user!);
    }

    private static AuthRequestPayload? DeserializeAuthPayload(MessageEnvelope message)
    {
        if (!message.Payload.HasValue)
        {
            return null;
        }

        try
        {
            return message.Payload.Value.Deserialize<AuthRequestPayload>();
        }
        catch
        {
            return null;
        }
    }

    private static string GetOpponentName(GameRoom room, Guid playerId)
    {
        if (room.PlayerX?.Id == playerId)
        {
            return room.PlayerOName ?? "Đối thủ";
        }

        if (room.PlayerO?.Id == playerId)
        {
            return room.PlayerXName ?? "Đối thủ";
        }

        return "Đối thủ";
    }

    private async Task SaveMatchHistoryAsync(GameRoom room, GameStatus status)
    {
        AuthenticatedUser? playerXUser = room.PlayerX is not null &&
            _authenticatedUsers.TryGetValue(room.PlayerX.Id, out AuthenticatedUser? authenticatedX)
                ? authenticatedX
                : null;

        AuthenticatedUser? playerOUser = room.PlayerO is not null &&
            _authenticatedUsers.TryGetValue(room.PlayerO.Id, out AuthenticatedUser? authenticatedO)
                ? authenticatedO
                : null;

        string playerXName = room.PlayerX is not null && _playerNames.TryGetValue(room.PlayerX.Id, out var nameX)
            ? nameX
            : room.PlayerXName ?? "Player X";

        string playerOName = room.PlayerO is not null && _playerNames.TryGetValue(room.PlayerO.Id, out var nameO)
            ? nameO
            : room.PlayerOName ?? "Player O";

        if (_matchHistoryStore is not null)
        {
            try
            {
                string? winnerName = status switch
                {
                    GameStatus.XWon => playerXName,
                    GameStatus.OWon => playerOName,
                    _ => null
                };

                Guid? winnerUserId = status switch
                {
                    GameStatus.XWon => playerXUser?.UserId,
                    GameStatus.OWon => playerOUser?.UserId,
                    _ => null
                };

                var moves = room.GetMoveHistory();
                var now = DateTime.UtcNow;

                var matchMoves = moves.Select((m, i) =>
                    new MatchMoveRecord(i + 1, m.PlayerName, m.Row, m.Col, m.Timestamp))
                    .ToList();

                var record = new MatchRecord(
                    Guid.NewGuid(),
                    room.RoomId,
                    playerXName,
                    playerOName,
                    winnerName,
                    room.StartedAtUtc,
                    now,
                    matchMoves)
                {
                    PlayerXUserId = playerXUser?.UserId,
                    PlayerOUserId = playerOUser?.UserId,
                    WinnerUserId = winnerUserId
                };

                await _matchHistoryStore.SaveMatchAsync(record);
                Console.WriteLine($"[HISTORY] Saved match {record.MatchId} in room {room.RoomId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HISTORY ERROR] {ex.Message}");
            }
        }

        if (_playerRecordStore is not null)
        {
            try
            {
                bool isDraw = status == GameStatus.Draw;
                await UpdatePlayerStatsAsync(playerXName, status == GameStatus.XWon, isDraw);
                await UpdatePlayerStatsAsync(playerOName, status == GameStatus.OWon, isDraw);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATISTICS ERROR] {ex.Message}");
            }
        }
    }

    private async Task UpdatePlayerStatsAsync(string playerName, bool isWinner, bool isDraw)
    {
        if (_playerRecordStore == null) return;

        string normalizedName = playerName.Trim();
        SemaphoreSlim playerLock = _playerRecordLocks.GetOrAdd(normalizedName, _ => new SemaphoreSlim(1, 1));

        await playerLock.WaitAsync();
        try
        {
            var record = await _playerRecordStore.GetAsync(normalizedName);

            int wins = record?.Wins ?? 0;
            int losses = record?.Losses ?? 0;
            int draws = record?.Draws ?? 0;

            if (isDraw)
            {
                draws++;
            }
            else if (isWinner)
            {
                wins++;
            }
            else
            {
                losses++;
            }

            var updatedRecord = new PlayerRecord(normalizedName, wins, losses, draws);
            await _playerRecordStore.SaveAsync(updatedRecord);
            Console.WriteLine($"[STATISTICS] Updated {normalizedName}: {wins}W - {losses}L - {draws}D");
        }
        finally
        {
            playerLock.Release();
        }
    }

    private async Task HandleRematchAsync(
        ClientSession session,
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUser(session, out _))
        {
            await SendErrorAsync(session, "Bạn cần đăng nhập trước khi chơi.", cancellationToken);
            return;
        }

        GameRoom? room = _roomManager.GetRoomBySession(session.Id);
        if (room is null)
        {
            await SendErrorAsync(session, "Bạn không ở trong phòng chơi nào.", cancellationToken);
            return;
        }

        var (success, bothAccepted, players) = room.HandleRematchRequest(session.Id);
        if (!success) return;

        if (bothAccepted)
        {
            var gameState = BuildGameStatePayload(room);

            foreach (var player in players)
            {
                try
                {
                    var symbol = room.GetPlayerSymbol(player.Id);
                    await player.SendAsync(new MessageEnvelope
                    {
                        Type = MessageType.RematchAccepted,
                        RoomId = room.RoomId,
                        PlayerId = player.Id.ToString(),
                        Payload = JsonSerializer.SerializeToElement(new
                        {
                            roomId = room.RoomId,
                            yourSymbol = symbol?.ToString() ?? "",
                            opponentName = GetOpponentName(room, player.Id),
                            board = gameState.Board,
                            currentTurnPlayerId = gameState.CurrentTurnPlayerId
                        })
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[REMATCH BROADCAST ERROR] {player.Id}: {ex.Message}");
                }
            }

            StartTurnTimeout(room);
        }
        else
        {
            var opponent = players.FirstOrDefault(p => p.Id != session.Id);
            if (opponent is not null)
            {
                await opponent.SendAsync(new MessageEnvelope
                {
                    Type = MessageType.ChatReceived,
                    RoomId = room.RoomId,
                    Payload = JsonSerializer.SerializeToElement(new ChatReceivedPayload
                    {
                        SenderName = "Hệ thống",
                        Message = "Đối thủ muốn chơi lại! Bấm Chơi lại để bắt đầu trận mới.",
                        Timestamp = DateTime.Now
                    })
                }, cancellationToken);
            }
        }
    }

    private sealed record AuthenticatedUser(
        Guid UserId,
        string Username,
        string DisplayName);
}
