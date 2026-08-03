using System.Text.Json.Serialization;

namespace Subconscious.Mobile.Engine;

/// <summary>Wire shapes shared with the engine's local API. Client-side copies of the DTOs in
/// <c>Subconscious.Engine.Api.DTOs</c>, kept in sync manually — same approach as
/// <c>Subconscious.Desktop.Engine.WireModels</c>, so the mobile client has no compile-time
/// coupling to Engine internals beyond what any external client would have.</summary>

public sealed record Workspace
{
    [JsonPropertyName("uuid")] public required string Uuid { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("defaultModelId")] public string? DefaultModelId { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
}

public sealed record ThreadInfo
{
    [JsonPropertyName("uuid")] public required string Uuid { get; init; }
    [JsonPropertyName("workspaceUuid")] public required string WorkspaceUuid { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("defaultModelId")] public string? DefaultModelId { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
}

public sealed record CreateWorkspaceRequest
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("defaultModelId")] public string? DefaultModelId { get; init; }
}
