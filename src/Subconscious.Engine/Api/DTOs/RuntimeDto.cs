namespace Subconscious.Engine.Api.DTOs;

/// <summary>
/// Runtime information response for the /runtime.json endpoint.
/// </summary>
public record RuntimeInfoResponse
{
    public required string Version { get; init; }
    public required string Status { get; init; }
    public required DateTime StartedAt { get; init; }
    public required List<string> AvailableModels { get; init; }
    public required List<string> RegisteredTools { get; init; }
    public required int ActiveSessions { get; init; }
}

/// <summary>
/// Health check response.
/// </summary>
public record HealthResponse
{
    public required string Status { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Model information DTO.
/// </summary>
public record ModelDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object>? Capabilities { get; init; }
}

/// <summary>
/// Tool information DTO.
/// </summary>
public record ToolDto
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Dictionary<string, object> Parameters { get; init; }
}
