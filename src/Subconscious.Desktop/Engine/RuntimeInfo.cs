using System.Text.Json.Serialization;

namespace Subconscious.Desktop.Engine;

/// <summary>
/// Deserialized shape of <c>runtime.json</c>. Field names/casing match
/// <see cref="Subconscious.Engine.Api.RuntimeInfoFile"/> exactly (camelCase on the wire) —
/// this is a client-side copy rather than a shared reference so the desktop client has no
/// compile-time coupling to Engine internals beyond what any external client would have.
/// </summary>
public sealed record RuntimeInfo
{
    [JsonPropertyName("host")]
    public required string Host { get; init; }

    [JsonPropertyName("port")]
    public required int Port { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }
}
