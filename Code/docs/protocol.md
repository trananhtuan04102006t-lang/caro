# Protocol v1

> **Updated**: feature/3-protocol-codec (PR #13)  
> This document reflects the protocol as actually implemented in `CaroNet.Shared`.

## Transport

- Game traffic: TCP using raw `System.Net.Sockets.Socket`.
- Optional LAN discovery: UDP broadcast in a later sprint.
- Payload encoding: UTF-8 JSON (`System.Text.Json`).
- Framing: 4-byte big-endian length prefix followed by the JSON payload.

```text
┌──────────────────────┬──────────────────────────────┐
│  length (4 bytes)    │  JSON payload (length bytes) │
│  big-endian int32    │  UTF-8 encoded               │
└──────────────────────┴──────────────────────────────┘
```

**Limits**: Maximum payload size is **1 048 576 bytes (1 MB)**. Frames exceeding
this limit are rejected by both `ProtocolFrameCodec` and `ProtocolFrameReader`.

## Message Envelope

Every message is wrapped in a `MessageEnvelope`:

```json
{
  "type":      "MakeMove",
  "requestId": "550e8400-e29b-41d4-a716-446655440000",
  "roomId":    "room-001",
  "playerId":  "player-001",
  "payload":   {}
}
```

| Field       | Type     | Required | Description                          |
|-------------|----------|----------|--------------------------------------|
| `type`      | string   | ✅        | Message type name (see table below)  |
| `requestId` | string?  | optional | UUID for correlating request/response|
| `roomId`    | string?  | optional | Room identifier                      |
| `playerId`  | string?  | optional | Player identifier                    |
| `payload`   | object?  | optional | Type-specific payload object         |

## Message Types

### Client → Server

| Value | Name         | Enum int | Payload DTO            |
|-------|--------------|----------|------------------------|
| `Hello`       | Handshake     | 1 | `HelloPayload`         |
| `CreateRoom`  | Create room   | 2 | `CreateRoomPayload`    |
| `JoinRoom`    | Join room     | 3 | `JoinRoomPayload`      |
| `Ready`       | Ready to play | 4 | _(none)_               |
| `MakeMove`    | Place piece   | 5 | `MakeMovePayload`      |
| `Chat`        | Chat message  | 6 | _(future)_             |
| `Heartbeat`   | Keep-alive    | 7 | _(none)_               |
| `Reconnect`   | Reconnect     | 8 | _(future)_             |

### Server → Client

| Value | Name               | Enum int | Payload DTO           |
|-------|--------------------|----------|-----------------------|
| `HelloAccepted`    | Handshake OK    | 50 | _(none)_           |
| `RoomListUpdated`  | Room list sync  | 51 | _(future)_         |
| `RoomJoined`       | Joined a room   | 52 | _(none)_           |
| `GameStarted`      | Match started   | 53 | `GameStatePayload` |
| `MoveAccepted`     | Move accepted   | 54 | `GameStatePayload` |
| `MoveRejected`     | Move rejected   | 55 | `ErrorPayload`     |
| `GameStateUpdated` | Board updated   | 56 | `GameStatePayload` |
| `GameEnded`        | Match ended     | 57 | `GameStatePayload` |
| `ChatReceived`     | Chat received   | 58 | _(future)_         |
| `Error`            | Generic error   | 100| `ErrorPayload`     |

## Payload DTOs

### `HelloPayload`
```json
{ "playerName": "Alice" }
```

### `CreateRoomPayload`
```json
{ "roomName": "room-001" }
```

### `JoinRoomPayload`
```json
{ "roomId": "room-001" }
```

### `MakeMovePayload`
```json
{ "row": 7, "col": 7 }
```

### `GameStatePayload`
```json
{
  "board":      [[0,0,...], ...],
  "currentTurn": "Alice",
  "winner":     null
}
```

### `ErrorPayload`
```json
{ "message": "Room not found." }
```

## Error Handling

- **Malformed JSON**: Server closes the connection after logging `[PROTOCOL ERROR]`.
- **Unsupported message type**: `InvalidOperationException` thrown; connection closed.
- **Oversized frame** (> 1 MB): Rejected immediately by `ProtocolFrameReader` before buffering payload bytes.
- **Frame length mismatch**: `ProtocolFrameCodec.Decode` throws `InvalidOperationException`.

## Socket Implementation Notes

- Keep one receive loop per connected socket (`ClientSession.RunAsync`).
- Serialize all sends through `SemaphoreSlim` per socket (`ClientSession._sendLock`).
- Use `CancellationToken` for graceful server shutdown.
- Log malformed frames, unsupported message types, and disconnect events.
- Never trust board state sent from the client; the server validates moves against its own state.
