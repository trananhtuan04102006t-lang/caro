using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class CreateRoomPayload
{
    [JsonPropertyName("roomName")]
    public string RoomName { get; init; } = string.Empty;
}