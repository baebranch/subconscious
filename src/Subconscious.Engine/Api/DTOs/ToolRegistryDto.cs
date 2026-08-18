namespace Subconscious.Engine.Api.DTOs;

/// <summary>Public representation of a persisted configured tool. No secret material is stored or returned.</summary>
public sealed record ToolRegistryDto
{
    public required int Id { get; init; }
    public required string Uuid { get; init; }
    public required string Name { get; init; }
    public string? Alias { get; init; }
    public string? Description { get; init; }
    public required string ToolType { get; init; }
    public string? ScriptPath { get; init; }
    public string? ScriptLanguage { get; init; }
    public string? EndpointUrl { get; init; }
    public string? AuthType { get; init; }
    /// <summary>Whether an encrypted API key is stored for an API-key-authenticated tool.</summary>
    public bool HasApiKey { get; init; }
    public string? AuthEnvVar { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Create or replace configured tool metadata and an optional write-only API key.</summary>
public sealed record UpsertToolRegistryRequest
{
    public string? Name { get; init; }
    public string? Alias { get; init; }
    public string? Description { get; init; }
    public string? ToolType { get; init; }
    public string? ScriptPath { get; init; }
    public string? ScriptLanguage { get; init; }
    public string? EndpointUrl { get; init; }
    public string? AuthType { get; init; }
    /// <summary>Write-only encrypted credential. This value is never included in a response.</summary>
    public string? ApiKey { get; init; }
    /// <summary>Removes a stored API key when true; ignored when a replacement key is supplied.</summary>
    public bool ClearApiKey { get; init; }
    public string? AuthEnvVar { get; init; }
    public string? Status { get; init; }
}

/// <summary>A built-in tool catalog item derived from <c>BaseToolRegistry.Catalog()</c>.</summary>
public sealed record BuiltinToolCatalogEntryDto
{
    public required string Name { get; init; }
    public required string Doc { get; init; }
    /// <summary>Stable lower-case operation category: <c>query</c> or <c>mutation</c>.</summary>
    public required string Operation { get; init; }
}

/// <summary>Built-in groups together with the user's persisted configured tools.</summary>
public sealed record ToolCatalogDto
{
    public required IReadOnlyDictionary<string, IReadOnlyList<BuiltinToolCatalogEntryDto>> Builtin { get; init; }
    public required IReadOnlyList<ToolRegistryDto> Configured { get; init; }
}
