using System.Text.Json;
using System.Text.Json.Serialization;

namespace Subconscious.Mobile.Engine;

/// <summary>Wire shapes shared with the engine's local API. Client-side copies of the DTOs in
/// <c>Subconscious.Engine.Api.DTOs</c>, kept in sync manually — same approach as
/// <c>Subconscious.Desktop.Engine.WireModels</c>, so the mobile client has no compile-time
/// coupling to Engine internals beyond what any external client would have.</summary>

public sealed record Workspace
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("uuid")] public required string Uuid { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("defaultModelId")] public string? DefaultModelId { get; init; }
    [JsonPropertyName("toolsConfig")] public string? ToolsConfig { get; init; }
    [JsonPropertyName("directories")] public string? Directories { get; init; }
    [JsonPropertyName("approvalConfig")] public string? ApprovalConfig { get; init; }
    [JsonPropertyName("ragConfig")] public string? RagConfig { get; init; }
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
    [JsonPropertyName("toolsConfig")] public string? ToolsConfig { get; init; }
    [JsonPropertyName("directories")] public string? Directories { get; init; }
    [JsonPropertyName("approvalConfig")] public string? ApprovalConfig { get; init; }
    [JsonPropertyName("ragConfig")] public string? RagConfig { get; init; }
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

public sealed record UpdateThreadRequest
{
    [JsonPropertyName("defaultModelId")] public required string DefaultModelId { get; init; }
}

public sealed record WsFrame
{
    [JsonPropertyName("v")] public int V { get; init; } = 1;
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("data")] public JsonElement? Data { get; init; }
}
