namespace Subconscious.Engine.Api.DTOs;

/// <summary>
/// Data transfer object for thread (conversation) information.
/// </summary>
public record ThreadDto
{
    /// <summary>Stable database identifier retained for app-state compatibility.</summary>
    public required int Id { get; init; }
    public required string Uuid { get; init; }
    public required string WorkspaceUuid { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? DefaultModelId { get; init; }
    /// <summary>Effective JSON configuration after the sparse thread override is overlaid on its workspace.</summary>
    public string? ToolsConfig { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a new thread.
/// </summary>
public record CreateThreadRequest
{
    public required string WorkspaceUuid { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? DefaultModelId { get; init; }
}

/// <summary>
/// Request to update an existing thread.
/// </summary>
public record UpdateThreadRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? DefaultModelId { get; init; }
}
