namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// A to-do item created by the user or agent, scoped to a workspace.
/// Status values: 'open', 'in_progress', 'done', 'cancelled'
/// Priority values: 'low', 'normal', 'high', 'urgent'
/// </summary>
public class TodoItem
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    /// <summary>
    /// Optional origin thread - which conversation created this todo.
    /// </summary>
    public int? ThreadId { get; set; }
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public required string Status { get; set; } = "open";
    public required string Priority { get; set; } = "normal";
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public Thread? Thread { get; set; }
}
