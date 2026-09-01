using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol;

public sealed class MessageEnvelope
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required MessageType Type { get; init; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; init; }

    [JsonPropertyName("roomId")]
    public string? RoomId { get; init; }

    [JsonPropertyName("playerId")]
    public string? PlayerId { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}