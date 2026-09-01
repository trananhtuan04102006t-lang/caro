using CaroNet.Shared.Game;
using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class GameEndedPayload
{
    [JsonPropertyName("winnerPlayerId")]
    public string? WinnerPlayerId { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("board")]
    public string[][] Board { get; init; } = [];

    [JsonPropertyName("winningCells")]
    public IReadOnlyList<BoardPosition>? WinningCells { get; init; }
}
