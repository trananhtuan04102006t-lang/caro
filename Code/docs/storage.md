# Storage Contracts

## Scope

Storage hiện tại cung cấp contract dùng chung, in-memory implementation để test nhanh và SQLite implementation để lưu lịch sử/best records thật trên máy local.

Đã làm:

- `MatchRecord`: lưu room id, người chơi X/O, người thắng, thời gian bắt đầu/kết thúc và danh sách nước đi.
- `MatchMoveRecord`: lưu thứ tự nước đi, người đánh, tọa độ và thời gian.
- `InMemoryMatchHistoryStore`: lưu, đọc theo match id, đọc toàn bộ theo thứ tự mới trước và lọc theo người chơi.
- `PlayerRecord`: lưu thống kê thắng/thua/hòa và tính `TotalGames`, `WinRate`.
- `InMemoryPlayerRecordStore`: lưu best records và lấy top player theo win rate, số trận thắng, rồi tên.
- `DatabaseInitializer`: tạo schema SQLite tối thiểu cho history và best records.
- `SqliteMatchHistoryStore`: lưu/đọc lịch sử trận bằng SQLite.
- `SqlitePlayerRecordStore`: lưu/đọc best records bằng SQLite.

Chưa làm:

- UI xem lịch sử trận.
- Replay/leaderboard nâng cao.

## Validation

Store chỉ nhận match đã kết thúc. Nếu `EndedAtUtc` chưa có, store ném lỗi thay vì tự tạo dữ liệu giả.

In-memory store trả snapshot riêng để caller không sửa trực tiếp dữ liệu đang giữ trong store.

SQLite store dùng transaction khi lưu match để header và danh sách nước đi đi cùng nhau. Nếu lưu lại cùng một match, danh sách nước đi cũ được xóa trước để không nhân đôi dữ liệu.

## Verification

```powershell
dotnet test .\Code\CaroNet.slnx -c Debug -p:Platform=x64
dotnet build .\Code\CaroNet.slnx -c Debug -p:Platform=x64
```

Evidence ngày 2026-06-14:

- Tests passed: 56 total, 0 failed.
- Build passed: 0 warnings, 0 errors.
