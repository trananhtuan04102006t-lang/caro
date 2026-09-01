using System;
using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public class ChatReceivedPayload
{
    [JsonPropertyName("senderPlayerId")]
    public string? SenderPlayerId { get; set; }

    [JsonPropertyName("senderName")]
    public string SenderName { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
