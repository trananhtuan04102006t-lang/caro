using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class HelloPayload
{
    [JsonPropertyName("playerName")]
    public string PlayerName { get; init; } = string.Empty;
}