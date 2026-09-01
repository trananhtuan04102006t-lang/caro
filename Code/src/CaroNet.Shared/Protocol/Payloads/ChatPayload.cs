using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public class ChatPayload
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}