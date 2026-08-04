namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// A conversation thread within a workspace.
/// </summary>
public class Thread
{
    public int Id { get; set; }
    public required string Uuid { get; set; }
    public int WorkspaceId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// Default model id for this thread. NULL or \"default\" means use workspace/global default.
    /// </summary>
    public string? DefaultModelId { get; set; }
    public string? ToolsConfig { get; set; }
    public string? SkillsConfig { get; set; }
    /// <summary>
    /// JSON {\"query\": bool, \"mutation\": bool} — HITL approval policy for this thread.
    /// </summary>
    public string? ApprovalConfig { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<TodoItem> TodoItems { get; set; } = new List<TodoItem>();
    public ICollection<WorkspaceMemory> Memories { get; set; } = new List<WorkspaceMemory>();
}
