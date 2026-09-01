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
