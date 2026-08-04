namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// User/agent-created notes scoped to a workspace.
/// Unlike memory these are human-readable documents, not key/value pairs.
/// </summary>
public class Note
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; } = string.Empty;
    /// <summary>
    /// Comma-separated tag list for categorization.
    /// </summary>
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
}
