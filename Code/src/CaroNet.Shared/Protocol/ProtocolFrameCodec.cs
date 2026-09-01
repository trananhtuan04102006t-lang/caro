using System.Buffers.Binary;
using System.Text.Json;

namespace CaroNet.Shared.Protocol;

public static class ProtocolFrameCodec
{
    public const int MaxPayloadLength = 1024 * 1024; // 1 MB

    public static byte[] Encode(MessageEnvelope envelope)
    {
        byte[] payloadBytes =
            JsonSerializer.SerializeToUtf8Bytes(envelope);

        if (payloadBytes.Length > MaxPayloadLength)
        {
            throw new InvalidOperationException(
                $"Payload exceeds maximum size of {MaxPayloadLength} bytes.");
        }

        byte[] frame =
            new byte[4 + payloadBytes.Length];

        BinaryPrimitives.WriteInt32BigEndian(
            frame.AsSpan(0, 4),
            payloadBytes.Length);

        payloadBytes.CopyTo(frame.AsSpan(4));

        return frame;
    }

    public static MessageEnvelope Decode(
        ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
        {
            throw new InvalidOperationException(
                "Frame is too short.");
        }

        int payloadLength =
            BinaryPrimitives.ReadInt32BigEndian(
                frame[..4]);

        if (payloadLength > MaxPayloadLength)
        {
            throw new InvalidOperationException(
                $"Payload exceeds maximum size of {MaxPayloadLength} bytes.");
        }

        if (payloadLength != frame.Length - 4)
        {
            throw new InvalidOperationException(
                "Frame length mismatch.");
        }

        ReadOnlySpan<byte> payload =
            frame[4..];

        MessageEnvelope? envelope;

        try
        {
            envelope =
                JsonSerializer.Deserialize<MessageEnvelope>(
                    payload);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Malformed protocol message.",
                ex);
        }

        if (envelope is null)
        {
            throw new InvalidOperationException(
                "Invalid message.");
        }

        if (!Enum.IsDefined(envelope.Type))
        {
            throw new InvalidOperationException(
                $"Unsupported message type: {envelope.Type}");
        }

        return envelope;
    }
}