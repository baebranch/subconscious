using System.Text.Json.Serialization;

namespace Subconscious.Mobile.Engine;

/// <summary>
/// Deserialized shape of <c>runtime.json</c>. Client-side copy of the engine's
/// <c>RuntimeInfoFile</c> DTO (camelCase on the wire) — mirrors
/// <c>Subconscious.Desktop.Engine.RuntimeInfo</c> so both clients discover the engine the same
/// way. See <see cref="EngineDiscovery"/> for the caveat on mobile targets: this file-based
/// discovery only makes sense on platforms with access to the same filesystem as the engine
/// (Windows/MacCatalyst here), not sandboxed Android/iOS.
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
