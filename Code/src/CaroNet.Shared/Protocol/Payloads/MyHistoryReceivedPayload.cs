using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class MyHistoryReceivedPayload
{
    [JsonPropertyName("matches")]
    public IReadOnlyList<MyHistoryMatchPayload> Matches { get; init; } = [];
}

public sealed class MyHistoryMatchPayload
{
    [JsonPropertyName("roomId")]
    public string RoomId { get; init; } = string.Empty;

    [JsonPropertyName("playerXName")]
    public string PlayerXName { get; init; } = string.Empty;

    [JsonPropertyName("playerOName")]
    public string PlayerOName { get; init; } = string.Empty;

    [JsonPropertyName("winnerName")]
    public string? WinnerName { get; init; }

    [JsonPropertyName("playedAtUtc")]
    public DateTime PlayedAtUtc { get; init; }

    [JsonPropertyName("moveCount")]
    public int MoveCount { get; init; }
}
