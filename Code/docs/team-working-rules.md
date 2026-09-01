# Quy Tắc Làm Việc Nhóm CaroNet

Tài liệu này là quy chuẩn làm việc chung của nhóm CaroNet. Mục tiêu là giúp mọi người biết rõ mình phải làm gì, làm theo nhánh nào, mở Pull Request ra sao, code theo chuẩn nào, và khi nào một task mới được xem là hoàn thành.

## 1. Mục Tiêu Hiện Tại

Ưu tiên cao nhất của nhóm là hoàn thành **P0 - Playable TCP Match**:

```text
Chạy server, mở 2 WinUI client, tạo/join room, đánh cờ theo lượt qua TCP,
server kiểm tra nước đi, broadcast bàn cờ cho cả hai client, chơi hết một ván.
```

Trong P0, không làm lan scope:

- Không làm UDP LAN discovery.
- Không làm leaderboard.
- Không làm reconnect hoàn chỉnh.
- Không làm animation/theme phức tạp.
- Không làm AI chơi cờ.
- Không để client tự quyết định thắng/thua.

## 2. Priority Labels

Mỗi issue phải có một label độ ưu tiên.

| Label | Ý nghĩa | Quy tắc |
| --- | --- | --- |
| `P0` | Bắt buộc để demo playable TCP match | Làm trước, không để P1/P2 làm chậm P0 |
| `P1` | Nên có sau khi P0 chạy ổn | Chỉ làm khi không ảnh hưởng P0 |
| `P2` | Bonus hoặc polish | Chỉ làm khi P0/P1 đã ổn định |

P0 hiện tại chỉ gồm:

- Rule engine.
- Protocol envelope và length-prefixed codec.
- Raw socket server sessions.
- Room manager create/join/move/broadcast.
- Client socket service.
- Client ViewModel/state contract.
- WinUI playable board.

P1 gồm:

- Chat trong phòng.
- Heartbeat/disconnect cơ bản.
- Match history storage/SQLite.
- Trạng thái kết nối rõ ràng.
- Cập nhật README/protocol docs theo implementation thật.

P2 gồm:

- Test LAN.
- UI polish.
- Log và edge cases nâng cao.
- Reconnect ngắn hạn nếu kịp.
- Chuẩn bị demo/vấn đáp nâng cao.

## 3. Area Labels

Mỗi issue phải có ít nhất một label mảng công việc.

| Label | Dùng cho |
| --- | --- |
| `area:game` | Rule engine, board, turn, win/draw, game state |
| `area:protocol` | Message type, DTO, JSON serialization, length-prefix framing |
| `area:server` | Server host, socket sessions, dispatcher, room manager |
| `area:client-net` | Client socket service, connect/send/receive/disconnect |
| `area:ui-contract` | ViewModel, state mapping, command giữa UI và service |
| `area:ui` | WinUI screens, controls, rendering, user interaction |
| `area:storage` | Match history, SQLite, repository/store |
| `area:test` | Automated test, manual test, demo rehearsal evidence |
| `area:docs` | README, protocol, architecture, run guide, reports |

Trạng thái đặc biệt:

| Label | Dùng khi nào |
| --- | --- |
| `blocked` | Task đang kẹt và cần leader can thiệp |
| `needs-review` | Task hoặc PR cần được review trước khi tiếp tục |

## 4. Milestones

| Milestone | Deadline | Mục tiêu |
| --- | --- | --- |
| `P0 - Playable TCP Match` | 2026-06-09 | Server + 2 WinUI client chơi được 1 ván qua TCP |
| `P1 - Network Features and Match History` | 2026-06-16 | Chat, heartbeat/disconnect, storage, connection status |
| `P2 - LAN Testing and Stability` | 2026-06-23 | Test LAN, fix bug, edge cases, UI polish vừa đủ |
| `Final - Release and Defense` | 2026-06-30 | Release, README cuối, test report, demo script, vấn đáp |

## 5. GitFlow

Các nhánh chính:

- `main`: bản ổn định để demo hoặc nộp.
- `develop`: nhánh tích hợp chính của nhóm.
- `feature/<issue-number>-<short-name>`: nhánh làm task mới.
- `bugfix/<issue-number>-<short-name>`: nhánh sửa lỗi.

Ví dụ tạo branch:

```powershell
git checkout develop
git pull origin develop
git checkout -b feature/3-protocol-codec
```

Không được:

- Không push trực tiếp vào `main`.
- Không push trực tiếp vào `develop`.
- Không làm task khi chưa có GitHub Issue.
- Không merge Pull Request của chính mình.
- Không làm 2 issue cùng lúc nếu issue cũ chưa có tiến độ rõ ràng.

## 6. Quy Trình Làm Task

Mỗi task phải đi theo flow:

```text
Issue -> Branch -> Code -> Build/Test -> Pull Request -> Review -> Merge -> Done
```

Trước khi code:

```powershell
git checkout develop
git pull origin develop
git checkout -b feature/<issue-number>-<short-name>
```

Trước khi mở Pull Request:

```powershell
dotnet build .\Code\CaroNet.slnx -c Debug -p:Platform=x64
dotnet test .\Code\CaroNet.slnx -c Debug -p:Platform=x64
```

Commit message nên dùng:

```text
feat(game): add rule engine
feat(protocol): add length-prefixed codec
fix(server): handle client disconnect
test(game): add diagonal win tests
docs(protocol): update message schema
```

## 7. Pull Request Rules

Pull Request phải có:

- Link issue, ví dụ `Closes #3`.
- Mô tả đã thay đổi gì.
- Cách test.
- Evidence: test output, log, screenshot hoặc manual steps.
- Nếu dùng AI, ghi rõ dùng để làm gì và đã verify bằng gì.

Không merge nếu:

- Build/test fail.
- PR không link issue.
- Không có evidence.
- Code sai scope issue.
- Người làm không giải thích được code.

## 8. Quy Tắc Kiến Trúc

Server là nguồn dữ liệu chính của ván đấu.

Client chỉ được:

- Gửi request.
- Nhận state server xác nhận.
- Hiển thị state.

Client không được:

- Tự quyết định thắng/thua.
- Tự sửa board sau khi click mà chưa có server broadcast.
- Gọi socket trực tiếp từ UI.

Flow đúng:

```text
WinUI View
-> ViewModel
-> Client service
-> TCP socket
-> Server session
-> Message dispatcher
-> GameRoom
-> Rule engine
-> Broadcast GameStateUpdated
-> ViewModel update
-> UI render
```

## 9. Quy Tắc Đặt Tên C#

Dùng chuẩn C#/.NET:

| Thành phần | Quy tắc | Ví dụ |
| --- | --- | --- |
| Class, record, enum | `PascalCase` | `CaroRuleEngine` |
| Method, property | `PascalCase` | `ApplyMove` |
| Local variable, parameter | `camelCase` | `currentTurn` |
| Private field | `_camelCase` | `_boardSize` |
| Async method | Kết thúc bằng `Async` | `SendAsync` |
| Interface | Bắt đầu bằng `I` | `IClientConnection` |

Ví dụ:

```csharp
public sealed class CaroRuleEngine
{
    private readonly int _boardSize;

    public MoveResult ApplyMove(BoardPosition position, PlayerSymbol player)
    {
        var isInsideBoard = IsInsideBoard(position);
        // ...
    }

    public Task SendAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        // ...
    }
}
```

Tên phải rõ nghĩa:

- Tốt: `currentTurn`, `roomId`, `playerSymbol`, `MoveRejected`.
- Không tốt: `x`, `data`, `temp`, `handle`, `abc`.

## 10. Quy Tắc Code

Ưu tiên:

- Code nhỏ, dễ test.
- Mỗi class có một trách nhiệm rõ.
- Logic game/protocol phải nằm ngoài WinUI.
- Dùng `CancellationToken` cho network async.
- Một socket chỉ có một receive loop.
- Gửi socket phải qua lock hoặc queue để tránh trộn bytes.

Không được:

- Không hardcode lung tung nếu có thể đưa vào constant/config.
- Không để file quá lớn mà không có lý do.
- Không copy logic giữa client và server.
- Không swallow exception im lặng.
- Không dùng `async void` trừ event handler UI.
- Không block UI thread bằng `.Wait()` hoặc `.Result`.

## 11. Quy Tắc Test

P0 bắt buộc có test cho:

- Rule engine.
- Protocol frame codec.
- Room/game state nếu có thể.

Manual test bắt buộc:

- Chạy server.
- Mở 2 client.
- Create/join room.
- Đánh vài nước.
- Sai lượt bị reject.
- Cả 2 client thấy cùng board state.

## 12. Quy Tắc Dùng AI

Được dùng AI để:

- Hỏi concept.
- Gợi ý test case.
- Review code.
- Tìm lỗi.
- Viết nháp code.

Không được:

- Paste code AI mà không hiểu.
- Submit code chưa chạy test.
- Nộp phần mình không giải thích được.
- Dùng AI để bịa log/test/evidence.

Khi review, owner phải trả lời được:

- Input là gì?
- Output là gì?
- Lỗi có thể xảy ra ở đâu?
- Test nào chứng minh đúng?
- Phần nào AI hỗ trợ và đã verify ra sao?

## 13. Definition of Done

Một task chỉ được xem là Done khi:

- Có Pull Request link issue.
- Build pass.
- Test hoặc manual evidence pass.
- Không phá flow P0.
- Docs được cập nhật nếu đổi protocol/model.
- Owner giải thích được phần mình làm trong 3 phút.

Nếu thiếu một trong các điều trên, task vẫn chưa Done.
