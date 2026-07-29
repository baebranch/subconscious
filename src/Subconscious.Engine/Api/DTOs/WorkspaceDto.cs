namespace Subconscious.Engine.Api.DTOs;

/// <summary>
/// Data transfer object for workspace information.
/// </summary>
public record WorkspaceDto
{
    public required string Uuid { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? DefaultModelId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a new workspace.
/// </summary>
public record CreateWorkspaceRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? DefaultModelId { get; init; }
}
