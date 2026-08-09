using System.Text.Json.Nodes;

namespace Subconscious.Engine.Api.DTOs;

/// <summary>Raw JSON configuration returned by the dedicated tool-config endpoints.</summary>
public sealed record ToolConfigDto
{
    public JsonNode? Config { get; init; }
}

/// <summary>Desired complete tool configuration for a workspace or thread.</summary>
public sealed record UpdateToolConfigRequest
{
    public required JsonNode Config { get; init; }
}
