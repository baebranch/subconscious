using System.Text;
using FluentAssertions;
using Subconscious.Engine.Agents.Bedrock;

namespace Subconscious.Engine.Tests.Agents.Bedrock;

public class AwsEventStreamDecoderTests
{
    private static async Task<List<AwsEventStreamFrame>> DecodeAllAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frames = new List<AwsEventStreamFrame>();
        await foreach (var frame in AwsEventStreamDecoder.DecodeAsync(stream))
        {
            frames.Add(frame);
        }
        return frames;
    }

    [Fact]
    public async Task DecodeAsync_SingleEventFrame_RoundTrips()
    {
        var payload = """{"delta":{"text":"hello"}}""";
        var bytes = AwsEventStreamEncoder.EncodeEvent("contentBlockDelta", payload);

        var frames = await DecodeAllAsync(bytes);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("contentBlockDelta");
        frames[0].MessageType.Should().Be("event");
        frames[0].PayloadAsText.Should().Be(payload);
    }

    [Fact]
    public async Task DecodeAsync_MultipleFrames_DecodesAllInOrder()
    {
        // Mirrors the shape of a real Bedrock converse-stream response.
        var frames = new[]
        {
            AwsEventStreamEncoder.EncodeEvent("messageStart", """{"role":"assistant"}"""),
            AwsEventStreamEncoder.EncodeEvent("contentBlockDelta", """{"delta":{"text":"Hel"}}"""),
            AwsEventStreamEncoder.EncodeEvent("contentBlockDelta", """{"delta":{"text":"lo"}}"""),
            AwsEventStreamEncoder.EncodeEvent("messageStop", """{"stopReason":"end_turn"}"""),
        };
        var concatenated = frames.SelectMany(f => f).ToArray();

        var decoded = await DecodeAllAsync(concatenated);

        decoded.Should().HaveCount(4);
        decoded.Select(f => f.EventType).Should()
            .Equal("messageStart", "contentBlockDelta", "contentBlockDelta", "messageStop");
        string.Concat(decoded
                .Where(f => f.EventType == "contentBlockDelta")
                .Select(f => System.Text.Json.JsonDocument.Parse(f.PayloadAsText)
                    .RootElement.GetProperty("delta").GetProperty("text").GetString()))
            .Should().Be("Hello");
    }

    [Fact]
    public async Task DecodeAsync_EmptyStream_YieldsNoFrames()
    {
        var frames = await DecodeAllAsync([]);

        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task DecodeAsync_EmptyPayload_IsSupported()
    {
        var bytes = AwsEventStreamEncoder.Encode(
            new Dictionary<string, string> { [":event-type"] = "ping" }, []);

        var frames = await DecodeAllAsync(bytes);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("ping");
        frames[0].Payload.Should().BeEmpty();
    }

    [Fact]
    public async Task DecodeAsync_CorruptedPreludeCrc_Throws()
    {
        var bytes = AwsEventStreamEncoder.EncodeEvent("contentBlockDelta", "{}");
        bytes[8] ^= 0xFF; // flip bits in the prelude CRC

        var act = async () => await DecodeAllAsync(bytes);

        await act.Should().ThrowAsync<InvalidDataException>()
            .Where(e => e.Message.Contains("prelude CRC32"));
    }

    [Fact]
    public async Task DecodeAsync_CorruptedPayload_FailsMessageCrc()
    {
        var bytes = AwsEventStreamEncoder.EncodeEvent("contentBlockDelta", """{"delta":{"text":"hi"}}""");
        // Corrupt a payload byte without touching the prelude, so only the message CRC catches it.
        bytes[^6] ^= 0xFF;

        var act = async () => await DecodeAllAsync(bytes);

        await act.Should().ThrowAsync<InvalidDataException>()
            .Where(e => e.Message.Contains("message CRC32"));
    }

    [Fact]
    public async Task DecodeAsync_TruncatedFrame_Throws()
    {
        var bytes = AwsEventStreamEncoder.EncodeEvent("contentBlockDelta", """{"delta":{"text":"hi"}}""");
        var truncated = bytes[..^3];

        var act = async () => await DecodeAllAsync(truncated);

        await act.Should().ThrowAsync<InvalidDataException>()
            .Where(e => e.Message.Contains("Truncated"));
    }

    [Fact]
    public async Task DecodeAsync_SseTextInsteadOfBinaryFrames_ThrowsRatherThanSilentlyMisparsing()
    {
        // Regression guard for the real-world failure mode where an SSE/JSON body is fed into a
        // binary event-stream parser: it must fail loudly, not produce garbage frames.
        var sse = Encoding.UTF8.GetBytes("data: {\"delta\":{\"text\":\"hello\"}}\n\ndata: [DONE]\n\n");

        var act = async () => await DecodeAllAsync(sse);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task DecodeAsync_AllHeaderValueTypes_AreParsed()
    {
        // Bedrock only emits string headers today, but the decoder must handle every spec'd type
        // so an unexpected header can't desynchronize the parse.
        var headerBlock = new List<byte>();
        void AddHeader(string name, byte type, byte[] value)
        {
            headerBlock.Add((byte)name.Length);
            headerBlock.AddRange(Encoding.UTF8.GetBytes(name));
            headerBlock.Add(type);
            headerBlock.AddRange(value);
        }

        AddHeader("t", 0, []);                                  // bool true
        AddHeader("f", 1, []);                                  // bool false
        AddHeader("b", 2, [0x7F]);                              // byte
        AddHeader("s", 3, [0x01, 0x00]);                        // int16 = 256
        AddHeader("i", 4, [0x00, 0x00, 0x01, 0x00]);            // int32 = 256
        AddHeader("l", 5, [0, 0, 0, 0, 0, 0, 0x01, 0x00]);      // int64 = 256

        var frame = BuildFrameWithRawHeaders(headerBlock.ToArray(), []);

        var frames = await DecodeAllAsync(frame);

        frames.Should().ContainSingle();
        var h = frames[0].Headers;
        h["t"].Should().Be("true");
        h["f"].Should().Be("false");
        h["b"].Should().Be("127");
        h["s"].Should().Be("256");
        h["i"].Should().Be("256");
        h["l"].Should().Be("256");
    }

    /// <summary>
    /// Builds a correctly-CRC'd frame from a pre-encoded raw header block, so tests can exercise
    /// header value types the (string-only) encoder does not emit.
    /// </summary>
    private static byte[] BuildFrameWithRawHeaders(byte[] headerBytes, byte[] payload)
    {
        var totalLength = 12 + headerBytes.Length + payload.Length + 4;
        var frame = new byte[totalLength];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), (uint)totalLength);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), (uint)headerBytes.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            frame.AsSpan(8, 4), System.IO.Hashing.Crc32.HashToUInt32(frame.AsSpan(0, 8)));
        headerBytes.CopyTo(frame.AsSpan(12));
        payload.CopyTo(frame.AsSpan(12 + headerBytes.Length));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            frame.AsSpan(totalLength - 4, 4),
            System.IO.Hashing.Crc32.HashToUInt32(frame.AsSpan(0, totalLength - 4)));
        return frame;
    }
}
