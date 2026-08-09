using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Subconscious.Desktop.Engine;

/// <summary>Wire shapes shared with the engine's local API. Client-side copies of the DTOs in
/// <c>Subconscious.Engine.Api.DTOs</c>, kept in sync manually the same way
/// <c>subconscious-code/src/engine/types.ts</c> tracks the Python API by hand.</summary>

public sealed record Workspace
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("uuid")] public required string Uuid { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("defaultModelId")] public string? DefaultModelId { get; init; }
    /// <summary>Raw persisted JSON retained until the workspace form serializes its edits.</summary>
    [JsonPropertyName("toolsConfig")] public string? ToolsConfig { get; init; }
    [JsonPropertyName("directories")] public string? Directories { get; init; }
    [JsonPropertyName("approvalConfig")] public string? ApprovalConfig { get; init; }
    [JsonPropertyName("ragConfig")] public string? RagConfig { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
}

/// <summary>A workspace-relative file or directory returned by the Engine.</summary>
public sealed record WorkspaceFileEntry
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("relativePath")] public required string RelativePath { get; init; }
    [JsonPropertyName("isDirectory")] public bool IsDirectory { get; init; }
}

/// <summary>UTF-8 content returned by the Engine for a workspace file.</summary>
public sealed record WorkspaceFileContent
{
    [JsonPropertyName("content")] public required string Content { get; init; }
}

/// <summary>Write-only content for an existing workspace-relative file.</summary>
public sealed record WriteWorkspaceFileRequest
{
    [JsonPropertyName("content")] public string? Content { get; init; }
}

public sealed record ThreadInfo
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("uuid")] public required string Uuid { get; init; }
    [JsonPropertyName("workspaceUuid")] public required string WorkspaceUuid { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("defaultModelId")] public string? DefaultModelId { get; init; }
    /// <summary>The effective workspace-plus-thread tool policy returned by the engine.</summary>
    [JsonPropertyName("toolsConfig")] public string? ToolsConfig { get; init; }
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

/// <summary>Redacted encrypted model configuration metadata from the engine.</summary>
public sealed record ModelConfiguration
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("alias")] public string? Alias { get; init; }
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; init; }
    [JsonPropertyName("contextWindow")] public int? ContextWindow { get; init; }
    [JsonPropertyName("hasApiKey")] public bool HasApiKey { get; init; }
}

/// <summary>Write-only API-key payload for an encrypted model configuration.</summary>
public sealed record UpsertModelConfigurationRequest
{
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("alias")] public string? Alias { get; init; }
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; init; }
    [JsonPropertyName("contextWindow")] public int? ContextWindow { get; init; }
    [JsonPropertyName("apiKey")] public string? ApiKey { get; init; }
    [JsonPropertyName("clearApiKey")] public bool ClearApiKey { get; init; }
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

/// <summary>Raw JSON configuration exchanged by the workspace and thread policy endpoints.</summary>
public sealed record ToolConfigResponse
{
    [JsonPropertyName("config")] public JsonNode? Config { get; init; }
}

public sealed record UpdateToolConfigRequest
{
    [JsonPropertyName("config")] public required JsonNode Config { get; init; }
}

public sealed record ToolRegistry
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("uuid")] public required string Uuid { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("alias")] public string? Alias { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("toolType")] public required string ToolType { get; init; }
    [JsonPropertyName("scriptPath")] public string? ScriptPath { get; init; }
    [JsonPropertyName("scriptLanguage")] public string? ScriptLanguage { get; init; }
    [JsonPropertyName("endpointUrl")] public string? EndpointUrl { get; init; }
    [JsonPropertyName("authType")] public string? AuthType { get; init; }
    [JsonPropertyName("authEnvVar")] public string? AuthEnvVar { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }
}

public sealed record UpsertToolRegistryRequest
{
    [JsonPropertyName("alias")] public string? Alias { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("toolType")] public string? ToolType { get; init; }
    [JsonPropertyName("scriptPath")] public string? ScriptPath { get; init; }
    [JsonPropertyName("scriptLanguage")] public string? ScriptLanguage { get; init; }
    [JsonPropertyName("endpointUrl")] public string? EndpointUrl { get; init; }
    [JsonPropertyName("authType")] public string? AuthType { get; init; }
    [JsonPropertyName("authEnvVar")] public string? AuthEnvVar { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
}

public sealed record BuiltinToolCatalogEntry
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("doc")] public required string Doc { get; init; }
    [JsonPropertyName("operation")] public string? Operation { get; init; }
}

public sealed record ToolCatalog
{
    [JsonPropertyName("builtin")] public required Dictionary<string, List<BuiltinToolCatalogEntry>> Builtin { get; init; }
    [JsonPropertyName("configured")] public required List<ToolRegistry> Configured { get; init; }
}

public sealed record CreateThreadRequest
{
    [JsonPropertyName("workspaceUuid")] public required string WorkspaceUuid { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("defaultModelId")] public string? DefaultModelId { get; init; }
}

/// <summary>Partial thread update used by the omnibox model selector.</summary>
public sealed record UpdateThreadRequest
{
    [JsonPropertyName("defaultModelId")] public required string DefaultModelId { get; init; }
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


/// <summary>A generic setting persisted by the engine's client- and tag-scoped app-state API.</summary>
public sealed record AppStateSetting
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("value")] public required string Value { get; init; }
    [JsonPropertyName("tag")] public string? Tag { get; init; }
    [JsonPropertyName("client")] public string? Client { get; init; }
}
