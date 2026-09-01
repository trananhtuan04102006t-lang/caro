using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class TopRecordsReceivedPayload
{
    [JsonPropertyName("players")]
    public IReadOnlyList<TopPlayerRecordPayload> Players { get; init; } = [];
}

public sealed class TopPlayerRecordPayload
{
    [JsonPropertyName("playerName")]
    public string PlayerName { get; init; } = string.Empty;

    [JsonPropertyName("wins")]
    public int Wins { get; init; }

    [JsonPropertyName("losses")]
    public int Losses { get; init; }

    [JsonPropertyName("draws")]
    public int Draws { get; init; }
}
