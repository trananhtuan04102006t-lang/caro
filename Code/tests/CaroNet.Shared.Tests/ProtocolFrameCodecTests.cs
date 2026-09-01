using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using CaroNet.Shared.Protocol;

namespace CaroNet.Shared.Tests;

public class ProtocolFrameCodecTests
{
    [Fact]
    public void Encode_Should_Write_Correct_BigEndian_Length_Prefix()
    {
        var message = new MessageEnvelope
        {
            Type = MessageType.Hello,
            Payload = JsonDocument.Parse("{}")
                .RootElement
                .Clone()
        };

        byte[] frame = ProtocolFrameCodec.Encode(message);

        int declaredLength =
            BinaryPrimitives.ReadInt32BigEndian(
                frame.AsSpan(0, 4));

        int actualPayloadLength =
            frame.Length - 4;

        Assert.Equal(
            actualPayloadLength,
            declaredLength);
    }

    [Fact]
    public void Encode_Then_Decode_Should_Preserve_MessageType()
    {
        var message = new MessageEnvelope
        {
            Type = MessageType.Hello,
            Payload = JsonDocument.Parse("{}")
                .RootElement
                .Clone()
        };

        byte[] frame = ProtocolFrameCodec.Encode(message);

        MessageEnvelope decoded =
            ProtocolFrameCodec.Decode(frame);

        Assert.Equal(
            MessageType.Hello,
            decoded.Type);
    }

    [Theory]
    [InlineData(MessageType.HelloAccepted)]
    [InlineData(MessageType.RoomJoined)]
    [InlineData(MessageType.GameStarted)]
    [InlineData(MessageType.GameStateUpdated)]
    [InlineData(MessageType.MoveRejected)]
    [InlineData(MessageType.GameEnded)]
    [InlineData(MessageType.Error)]
    public void Decode_Should_Accept_P0_Server_MessageTypes(MessageType messageType)
    {
        var message = new MessageEnvelope
        {
            Type = messageType,
            Payload = JsonDocument.Parse("{}")
                .RootElement
                .Clone()
        };

        byte[] frame = ProtocolFrameCodec.Encode(message);

        MessageEnvelope decoded = ProtocolFrameCodec.Decode(frame);

        Assert.Equal(messageType, decoded.Type);
    }

    [Fact]
    public void Decode_Should_Reject_Unsupported_MessageType()
    {
        string json =
            """
            {
                "type": "UnknownMessage"
            }
            """;

        byte[] payload =
            Encoding.UTF8.GetBytes(json);

        byte[] frame =
            new byte[4 + payload.Length];

        BinaryPrimitives.WriteInt32BigEndian(
            frame.AsSpan(0, 4),
            payload.Length);

        payload.CopyTo(frame.AsSpan(4));

        Assert.Throws<InvalidOperationException>(
            () => ProtocolFrameCodec.Decode(frame));
    }

    [Fact]
    public void Decode_Should_Reject_Invalid_Length_Prefix()
    {
        var message = new MessageEnvelope
        {
            Type = MessageType.Hello,
            Payload = JsonDocument.Parse("{}")
                .RootElement
                .Clone()
        };

        byte[] frame =
            ProtocolFrameCodec.Encode(message);

        frame[3]++;

        Assert.Throws<InvalidOperationException>(
            () => ProtocolFrameCodec.Decode(frame));
    }

    [Fact]
    public void Decode_Should_Reject_Frame_Too_Short()
    {
        byte[] frame = [1, 2, 3];

        Assert.Throws<InvalidOperationException>(
            () => ProtocolFrameCodec.Decode(frame));
    }

    [Fact]
    public void MessageEnvelope_Should_Use_Protocol_Field_Names()
    {
        var envelope = new MessageEnvelope
        {
            Type = MessageType.Hello
        };

        string json =
            JsonSerializer.Serialize(envelope);

        Assert.Contains("\"type\"", json);

        Assert.DoesNotContain("\"Type\"", json);
        Assert.DoesNotContain("\"RequestId\"", json);
        Assert.DoesNotContain("\"RoomId\"", json);
        Assert.DoesNotContain("\"PlayerId\"", json);
        Assert.DoesNotContain("\"Payload\"", json);
    }

    [Fact]
    public void Decode_Should_Reject_Oversized_Frame()
    {
        byte[] frame = new byte[4];

        BinaryPrimitives.WriteInt32BigEndian(
            frame,
            1024 * 1024 + 1);

        Assert.Throws<InvalidOperationException>(
            () => ProtocolFrameCodec.Decode(frame));
    }
}
