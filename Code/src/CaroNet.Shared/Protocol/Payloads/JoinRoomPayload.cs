using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class JoinRoomPayload
{
    [JsonPropertyName("roomId")]
    public string RoomId { get; init; } = string.Empty;
}