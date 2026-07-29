using System.Text.Json.Serialization;

namespace Subconscious.Desktop.Engine;

/// <summary>Wire shapes shared with the engine's local API. Client-side copies of the DTOs in
/// <c>Subconscious.Engine.Api.DTOs</c>, kept in sync manually the same way
/// <c>subconscious-code/src/engine/types.ts</c> tracks the Python API by hand.</summary>

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

public sealed record ChatMessage
{
    [JsonPropertyName("uuid")] public required string Uuid { get; init; }
    [JsonPropertyName("threadUuid")] public required string ThreadUuid { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
}

public sealed record ModelInfo
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
}

public sealed record CreateWorkspaceRequest
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
}

public sealed record CreateThreadRequest
{
    [JsonPropertyName("workspaceUuid")] public required string WorkspaceUuid { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
}

/// <summary>The <c>{ v, type, id?, data? }</c> WebSocket envelope, deserialized generically
/// (data kept as a raw <see cref="System.Text.Json.JsonElement"/> and reified per frame type
/// by the caller) since a single connection multiplexes many different frame shapes.</summary>
public sealed record WsFrame
{
    [JsonPropertyName("v")] public int V { get; init; } = 1;
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("data")] public System.Text.Json.JsonElement? Data { get; init; }
}
