using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class GameStatePayload
{
    [JsonPropertyName("currentTurnPlayerId")]
    public string CurrentTurnPlayerId { get; init; } =
        string.Empty;

    [JsonPropertyName("board")]
    public string[][] Board { get; init; } = [];

    [JsonPropertyName("isGameOver")]
    public bool IsGameOver { get; init; }

    [JsonPropertyName("winnerPlayerId")]
    public string? WinnerPlayerId { get; init; }
}
