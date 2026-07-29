namespace Subconscious.Engine.Agents.Bedrock;

/// <summary>
/// A single decoded frame from an AWS <c>application/vnd.amazon.eventstream</c> response.
/// Bedrock's <c>converse-stream</c> endpoint emits these instead of Server-Sent Events.
/// </summary>
/// <param name="Headers">Frame headers (e.g. <c>:event-type</c>, <c>:message-type</c>, <c>:content-type</c>).</param>
/// <param name="Payload">Raw payload bytes — for Bedrock this is a UTF-8 JSON document.</param>
public sealed record AwsEventStreamFrame(IReadOnlyDictionary<string, string> Headers, byte[] Payload)
{
    /// <summary>The <c>:event-type</c> header (e.g. "contentBlockDelta", "messageStop"), or null.</summary>
    public string? EventType => Headers.TryGetValue(":event-type", out var v) ? v : null;

    /// <summary>The <c>:message-type</c> header (e.g. "event", "exception"), or null.</summary>
    public string? MessageType => Headers.TryGetValue(":message-type", out var v) ? v : null;

    /// <summary>The payload decoded as UTF-8 text (Bedrock payloads are JSON).</summary>
    public string PayloadAsText => System.Text.Encoding.UTF8.GetString(Payload);
}
