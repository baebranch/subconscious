namespace Subconscious.Engine.Api.DTOs;

/// <summary>
/// Data transfer object for workspace information.
/// </summary>
public record WorkspaceDto
{
    /// <summary>Stable database identifier retained for app-state compatibility.</summary>
    public required int Id { get; init; }
    public required string Uuid { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? DefaultModelId { get; init; }
    /// <summary>Raw persisted JSON tool configuration.</summary>
    public string? ToolsConfig { get; init; }
    public string? Directories { get; init; }
    public string? ApprovalConfig { get; init; }
    public string? RagConfig { get; init; }
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
    public string? ToolsConfig { get; init; }
    public string? Directories { get; init; }
    public string? ApprovalConfig { get; init; }
    public string? RagConfig { get; init; }
}
