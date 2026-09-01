using System.Text.Json.Serialization;

namespace CaroNet.Shared.Protocol.Payloads;

public sealed class DrawResponsePayload
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }
}
