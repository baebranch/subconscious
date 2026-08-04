namespace Subconscious.Engine.Api.DTOs;

/// <summary>Redacted model configuration returned by the local API. API keys never leave the engine.</summary>
public sealed record ModelConfigurationDto
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public string? Alias { get; init; }
    public string? BaseUrl { get; init; }
    public int? ContextWindow { get; init; }
    public bool HasApiKey { get; init; }
}

/// <summary>Model configuration fields accepted by create and update routes.</summary>
public sealed record UpsertModelConfigurationRequest
{
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public string? Alias { get; init; }
    public string? BaseUrl { get; init; }
    public int? ContextWindow { get; init; }

    /// <summary>Write-only credential. This value is never included in a response.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Removes a stored credential when true; ignored when a replacement API key is supplied.</summary>
    public bool ClearApiKey { get; init; }
}
