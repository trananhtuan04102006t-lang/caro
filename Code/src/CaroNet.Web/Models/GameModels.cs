using CaroNet.Shared.Game;
using CaroNet.Shared.Game.AI;

namespace CaroNet.Web.Models;

public sealed record StartGameRequest(
    AiDifficulty Difficulty = AiDifficulty.Medium);


public sealed record MoveRequest(
    Guid GameId,
    int Row,
    int Column);


public sealed record MoveDto(
    int Row,
    int Column);


public sealed record GameResponse(
    Guid GameId,
    int Size,
    string CurrentPlayer,
    string Status,
    string Difficulty,
    string[] Board,
    MoveDto? PlayerMove,
    MoveDto? AiMove);