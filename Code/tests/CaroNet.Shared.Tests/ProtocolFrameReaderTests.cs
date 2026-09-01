using CaroNet.Shared.Protocol;

namespace CaroNet.Shared.Tests;

public class ProtocolFrameReaderTests
{
    [Fact]
    public void FrameReader_Should_Reassemble_Split_Frame()
    {
        var message = new MessageEnvelope
        {
            Type = MessageType.Hello
        };

        byte[] frame =
            ProtocolFrameCodec.Encode(message);

        var reader =
            new ProtocolFrameReader();

        reader.Append(frame[..2]);

        Assert.False(
            reader.TryReadFrame(out _));

        reader.Append(frame[2..5]);

        Assert.False(
            reader.TryReadFrame(out _));

        reader.Append(frame[5..]);

        Assert.True(
            reader.TryReadFrame(out byte[] result));

        Assert.Equal(
            frame,
            result);
    }
}