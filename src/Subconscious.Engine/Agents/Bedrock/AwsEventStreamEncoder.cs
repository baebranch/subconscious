using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace Subconscious.Engine.Agents.Bedrock;

/// <summary>
/// Encodes AWS <c>application/vnd.amazon.eventstream</c> frames (string headers only, which is
/// all Bedrock emits).
///
/// <para>
/// This is the inverse of <see cref="AwsEventStreamDecoder"/> and exists primarily so the
/// decoder can be tested against byte-exact, correctly-CRC'd frames without needing live AWS
/// credentials or a recorded fixture. It is deliberately part of the shipped assembly rather
/// than test-only code so the framing logic has exactly one definition of the wire format.
/// </para>
/// </summary>
public static class AwsEventStreamEncoder
{
    /// <summary>Encode a single frame with the given string headers and payload.</summary>
    public static byte[] Encode(IReadOnlyDictionary<string, string> headers, byte[] payload)
    {
        var headerBytes = EncodeHeaders(headers);
        var totalLength = 12 + headerBytes.Length + payload.Length + 4;
        var frame = new byte[totalLength];

        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)totalLength);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), (uint)headerBytes.Length);
        var preludeCrc = Crc32.HashToUInt32(frame.AsSpan(0, 8));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), preludeCrc);

        headerBytes.CopyTo(frame.AsSpan(12));
        payload.CopyTo(frame.AsSpan(12 + headerBytes.Length));

        var crc = Crc32.HashToUInt32(frame.AsSpan(0, totalLength - 4));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(totalLength - 4, 4), crc);

        return frame;
    }

    /// <summary>Encode a Bedrock-style event frame: <c>:message-type=event</c> plus an event type and JSON payload.</summary>
    public static byte[] EncodeEvent(string eventType, string jsonPayload) =>
        Encode(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [":message-type"] = "event",
                [":event-type"] = eventType,
                [":content-type"] = "application/json",
            },
            Encoding.UTF8.GetBytes(jsonPayload));

    private static byte[] EncodeHeaders(IReadOnlyDictionary<string, string> headers)
    {
        using var buffer = new MemoryStream();
        foreach (var (name, value) in headers)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length > byte.MaxValue)
            {
                throw new ArgumentException($"Header name '{name}' is too long to encode.", nameof(headers));
            }
            var valueBytes = Encoding.UTF8.GetBytes(value);
            if (valueBytes.Length > ushort.MaxValue)
            {
                throw new ArgumentException($"Header '{name}' value is too long to encode.", nameof(headers));
            }

            buffer.WriteByte((byte)nameBytes.Length);
            buffer.Write(nameBytes);
            buffer.WriteByte(7); // value type 7 = string
            var lengthPrefix = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)valueBytes.Length);
            buffer.Write(lengthPrefix);
            buffer.Write(valueBytes);
        }
        return buffer.ToArray();
    }
}
