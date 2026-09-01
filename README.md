# CaroNet AI

## Giới thiệu

CaroNet AI là hệ thống chơi Cờ Caro giữa người và máy,
được phát triển cho môn Trí tuệ nhân tạo.

Hệ thống sử dụng thuật toán Minimax kết hợp
Alpha-Beta Pruning để lựa chọn nước đi cho máy.

## Công nghệ

- C#
- .NET 10
- ASP.NET Core
- Minimax
- Alpha-Beta Pruning
- xUnit
- HTML/CSS/JavaScript

## Kiến trúc

```text
CaroNet.Web
     |
     v
CaroAiGameService
     |
     v
CaroNet.Shared
     |
     +-- CaroGameState
     |
     +-- MinimaxAiPlayer
     |
     +-- Alpha-Beta Pruning
Chạy project
1. Restore
dotnet restore src/CaroNet.Web/CaroNet.Web.csproj
2. Build
dotnet build src/CaroNet.Web/CaroNet.Web.csproj
3. Test
dotnet test tests/CaroNet.Shared.Tests/CaroNet.Shared.Tests.csproj
4. Chạy Web
dotnet run --project src/CaroNet.Web/CaroNet.Web.csproj --urls "http://0.0.0.0:5079"

Sau đó truy cập:

http://localhost:5079
Kiểm thử

Hiện tại bộ kiểm thử gồm 41 test.

41 succeeded
0 failed
Mục tiêu phát triển

Phát triển hệ thống người chơi đấu với máy,
trong đó AI sử dụng Minimax kết hợp Alpha-Beta Pruning
để tìm nước đi tốt nhất.


Lưu thành:

```text
README.md
