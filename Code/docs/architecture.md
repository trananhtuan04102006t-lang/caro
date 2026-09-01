# Architecture

CaroNet dùng mô hình client-server authoritative cho game Caro 1v1. Client chịu trách nhiệm UI và input; server chịu trách nhiệm phòng chơi, lượt đánh, kiểm tra luật và phát state mới cho người chơi.

## Project boundaries

- `CaroNet.Client.WinUI`: WinUI 3 application, views, view models, controls and client-facing services.
- `CaroNet.Server.Host`: server process, raw socket listener, client sessions, room manager and message dispatcher.
- `CaroNet.Shared`: shared models, game rules and protocol contracts.
- `CaroNet.Storage`: SQLite persistence for local profiles, match history and lightweight leaderboard data.

## Data flow

```text
View -> ViewModel -> Client service -> Socket connection -> Server dispatcher -> Game room -> Shared rules
```

The server sends confirmed state back to both clients after each valid move. Invalid client requests return an error message and must not mutate game state.

## Extension points

- Add UDP LAN discovery without changing game rules.
- Add SQLite repositories without changing protocol DTOs.
- Add reconnect support by keeping short-lived player session state in the server.
- Add spectator mode by reusing room snapshots and broadcast logic.
