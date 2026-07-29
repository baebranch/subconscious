using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace Subconscious.Engine.Agents.Bedrock;

/// <summary>
/// Decoder for the AWS <c>application/vnd.amazon.eventstream</c> binary framing used by
/// Amazon Bedrock's <c>converse-stream</c> endpoint.
///
/// <para>
/// This exists because Bedrock streaming is <b>not</b> Server-Sent Events — it is a binary,
/// CRC-checksummed frame protocol (see the Smithy "Amazon Event Stream Specification"). Every
/// other provider Subconscious talks to streams SSE, which is why LLM Tornado's provider
/// contract hands implementations a text <see cref="StreamReader"/>; this decoder therefore
/// reads from <see cref="StreamReader.BaseStream"/> to get at the raw bytes, because pushing
/// binary frames through text decoding would corrupt them.
/// </para>
///
/// <para>Frame layout (all integers big-endian):</para>
/// <code>
/// +----------------------------------------------------------+
/// | total length (4) | headers length (4) | prelude CRC32 (4) |
/// +----------------------------------------------------------+
/// | headers (headers length bytes)                            |
/// +----------------------------------------------------------+
/// | payload (total length - 16 - headers length bytes)         |
/// +----------------------------------------------------------+
/// | message CRC32 (4)                                         |
/// +----------------------------------------------------------+
/// </code>
/// </summary>
public static class AwsEventStreamDecoder
{
    /// <summary>Bytes of framing overhead per message: 3 x uint32 prelude + trailing uint32 CRC.</summary>
    private const int PreludeLength = 12;
    private const int MessageCrcLength = 4;
    private const int OverheadLength = PreludeLength + MessageCrcLength;

    /// <summary>Guards against a corrupt/hostile length prefix causing a huge allocation.</summary>
    private const int MaxFrameLength = 16 * 1024 * 1024;

    /// <summary>
    /// Asynchronously decode frames from <paramref name="stream"/> until it ends.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when a prelude or message CRC32 does not match, or a length prefix is implausible —
    /// i.e. the stream is corrupt or is not actually event-stream encoded.
    /// </exception>
    public static async IAsyncEnumerable<AwsEventStreamFrame> DecodeAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prelude = new byte[PreludeLength];

        while (true)
        {
            var read = await ReadExactlyOrEofAsync(stream, prelude, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                yield break; // clean end of stream
            }
            if (read < PreludeLength)
            {
                throw new InvalidDataException(
                    $"Truncated AWS event-stream prelude: expected {PreludeLength} bytes, got {read}.");
            }

            var totalLength = (int)BinaryPrimitives.ReadUInt32BigEndian(prelude.AsSpan(0, 4));
            var headersLength = (int)BinaryPrimitives.ReadUInt32BigEndian(prelude.AsSpan(4, 4));
            var preludeCrc = BinaryPrimitives.ReadUInt32BigEndian(prelude.AsSpan(8, 4));

            var computedPreludeCrc = Crc32.HashToUInt32(prelude.AsSpan(0, 8));
            if (computedPreludeCrc != preludeCrc)
            {
                throw new InvalidDataException(
                    $"AWS event-stream prelude CRC32 mismatch (expected 0x{preludeCrc:x8}, computed " +
                    $"0x{computedPreludeCrc:x8}). The stream is corrupt or is not event-stream encoded.");
            }

            if (totalLength < OverheadLength || totalLength > MaxFrameLength)
            {
                throw new InvalidDataException($"Implausible AWS event-stream frame length: {totalLength}.");
            }
            if (headersLength < 0 || headersLength > totalLength - OverheadLength)
            {
                throw new InvalidDataException(
                    $"Implausible AWS event-stream headers length: {headersLength} (frame total {totalLength}).");
            }

            var remaining = totalLength - PreludeLength;
            var rest = new byte[remaining];
            var restRead = await ReadExactlyOrEofAsync(stream, rest, cancellationToken).ConfigureAwait(false);
            if (restRead < remaining)
            {
                throw new InvalidDataException(
                    $"Truncated AWS event-stream frame: expected {remaining} more bytes, got {restRead}.");
            }

            var payloadLength = remaining - headersLength - MessageCrcLength;
            var messageCrc = BinaryPrimitives.ReadUInt32BigEndian(rest.AsSpan(remaining - MessageCrcLength, 4));

            // The message CRC covers the whole frame up to (but excluding) the CRC itself.
            // NOTE: use GetCurrentHashAsUInt32() rather than reading GetCurrentHash() bytes —
            // Crc32 emits its hash bytes little-endian, whereas the wire format stores the CRC
            // big-endian, so reading the byte form big-endian yields a byte-reversed value.
            var crc = new Crc32();
            crc.Append(prelude);
            crc.Append(rest.AsSpan(0, remaining - MessageCrcLength));
            var computedMessageCrc = crc.GetCurrentHashAsUInt32();
            if (computedMessageCrc != messageCrc)
            {
                throw new InvalidDataException(
                    $"AWS event-stream message CRC32 mismatch (expected 0x{messageCrc:x8}, computed " +
                    $"0x{computedMessageCrc:x8}).");
            }

            var headers = ParseHeaders(rest.AsSpan(0, headersLength));
            var payload = rest.AsSpan(headersLength, payloadLength).ToArray();

            yield return new AwsEventStreamFrame(headers, payload);
        }
    }

    /// <summary>
    /// Parse the header block. Each header is: 1-byte name length, name (UTF-8), 1-byte value
    /// type, then a type-dependent value. Bedrock only uses string headers in practice, but the
    /// other spec'd types are skipped correctly so an unexpected header can't desynchronize the
    /// parse.
    /// </summary>
    private static Dictionary<string, string> ParseHeaders(ReadOnlySpan<byte> headers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var offset = 0;

        while (offset < headers.Length)
        {
            var nameLength = headers[offset];
            offset += 1;
            if (offset + nameLength > headers.Length)
            {
                throw new InvalidDataException("Truncated AWS event-stream header name.");
            }
            var name = Encoding.UTF8.GetString(headers.Slice(offset, nameLength));
            offset += nameLength;

            if (offset >= headers.Length)
            {
                throw new InvalidDataException("Missing AWS event-stream header value type.");
            }
            var valueType = headers[offset];
            offset += 1;

            switch (valueType)
            {
                case 0: // bool true
                    result[name] = "true";
                    break;
                case 1: // bool false
                    result[name] = "false";
                    break;
                case 2: // byte
                    EnsureRemaining(headers, offset, 1);
                    result[name] = ((sbyte)headers[offset]).ToString();
                    offset += 1;
                    break;
                case 3: // int16
                    EnsureRemaining(headers, offset, 2);
                    result[name] = BinaryPrimitives.ReadInt16BigEndian(headers.Slice(offset, 2)).ToString();
                    offset += 2;
                    break;
                case 4: // int32
                    EnsureRemaining(headers, offset, 4);
                    result[name] = BinaryPrimitives.ReadInt32BigEndian(headers.Slice(offset, 4)).ToString();
                    offset += 4;
                    break;
                case 5: // int64
                case 8: // timestamp (int64 epoch millis)
                    EnsureRemaining(headers, offset, 8);
                    result[name] = BinaryPrimitives.ReadInt64BigEndian(headers.Slice(offset, 8)).ToString();
                    offset += 8;
                    break;
                case 6: // byte array
                case 7: // string
                {
                    EnsureRemaining(headers, offset, 2);
                    var length = BinaryPrimitives.ReadUInt16BigEndian(headers.Slice(offset, 2));
                    offset += 2;
                    EnsureRemaining(headers, offset, length);
                    result[name] = valueType == 7
                        ? Encoding.UTF8.GetString(headers.Slice(offset, length))
                        : Convert.ToBase64String(headers.Slice(offset, length));
                    offset += length;
                    break;
                }
                case 9: // uuid
                    EnsureRemaining(headers, offset, 16);
                    result[name] = new Guid(headers.Slice(offset, 16)).ToString();
                    offset += 16;
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unknown AWS event-stream header value type {valueType} for header '{name}'.");
            }
        }

        return result;
    }

    private static void EnsureRemaining(ReadOnlySpan<byte> span, int offset, int needed)
    {
        if (offset + needed > span.Length)
        {
            throw new InvalidDataException("Truncated AWS event-stream header value.");
        }
    }

    /// <summary>
    /// Fill <paramref name="buffer"/> from <paramref name="stream"/>, returning the number of
    /// bytes actually read. Returns 0 at a clean end of stream, and a short count only when the
    /// stream ended mid-buffer.
    /// </summary>
    private static async Task<int> ReadExactlyOrEofAsync(
        Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }
}
