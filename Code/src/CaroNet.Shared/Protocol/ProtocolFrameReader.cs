using System.Buffers.Binary;

namespace CaroNet.Shared.Protocol;

public sealed class ProtocolFrameReader
{
    // Giới hạn kích thước frame, khớp với ProtocolFrameCodec.MaxPayloadLength.
    private const int MaxPayloadLength = 1024 * 1024; // 1 MB

    private readonly List<byte> _buffer = [];

    public void Append(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
    }

    public bool TryReadFrame(out byte[] frame)
    {
        frame = [];

        if (_buffer.Count < 4)
        {
            return false;
        }

        int payloadLength =
            BinaryPrimitives.ReadInt32BigEndian(
                _buffer.Take(4).ToArray());

        if (payloadLength < 0 || payloadLength > MaxPayloadLength)
        {
            throw new InvalidOperationException(
                $"Invalid payload length: {payloadLength}. Maximum allowed is {MaxPayloadLength} bytes.");
        }

        int totalLength =
            4 + payloadLength;

        if (_buffer.Count < totalLength)
        {
            return false;
        }

        frame =
            _buffer.Take(totalLength)
                   .ToArray();

        _buffer.RemoveRange(
            0,
            totalLength);

        return true;
    }
}