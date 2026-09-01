# P0 WinUI Manual Evidence

Use this checklist when the server/client-net contract is ready.

## Build

```powershell
dotnet build .\Code\CaroNet.slnx -c Debug -p:Platform=x64
dotnet test .\Code\CaroNet.slnx -c Debug -p:Platform=x64
```

## Manual Flow

1. Run the server host.
2. Open two WinUI clients in Visual Studio x64.
3. Client A enters player name, then clicks Connect.
4. Client A clicks Create Room and records the room id.
5. Client B enters player name, then clicks Connect.
6. Client B enters the room id and clicks Join Room.
7. Client A clicks one board cell.
8. Verify both clients render the same X/O after server state broadcast.
9. Try a wrong-turn or occupied-cell move.
10. Verify the server rejection appears in the UI error area.

## Current UI Branch Notes

- The WinUI views call ViewModels/services, not raw sockets.
- The default app service now uses the real socket-backed client service. The local demo service is not the production path.
- Chat/history UI is intentionally excluded from P0.
- The client UI must not decide win/loss; final result must come from server state.

## 2026-06-13 Evidence

Automated verification:

```powershell
dotnet test .\Code\CaroNet.slnx -c Debug -p:Platform=x64
dotnet build .\Code\CaroNet.slnx -c Debug -p:Platform=x64
```

Result:

- Tests passed: 40 total, 0 failed.
- Build passed: 0 warnings, 0 errors.
- Covered `Connect` failure status, server `RoomJoined` mapping, server `GameStateUpdated` board rendering, `MoveRejected` error rendering, and `MakeMove` request sending without local board mutation.

Manual blocker:

- Full two-client playable evidence still depends on P0 server work in #4 and room/broadcast work in #9.
- Do not mark the full manual flow as passed until the server can create/join rooms and broadcast authoritative game state.
