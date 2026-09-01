using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class MakeMovePayload
{
    [JsonPropertyName("row")]
    public int Row { get; init; }

    [JsonPropertyName("column")]
    public int Column { get; init; }

    [JsonPropertyName("playerId")]
    public string PlayerId { get; init; } =
        string.Empty;
}