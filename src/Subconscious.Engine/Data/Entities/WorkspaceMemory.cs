namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// Long-term key/value memory scoped to a workspace.
/// The agent can store and retrieve facts that should persist across threads.
/// </summary>
public class WorkspaceMemory
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    /// <summary>
    /// Memory key (e.g., "user_name", "preferred_language")
    /// </summary>
    public required string Key { get; set; }
    public required string Value { get; set; }
    /// <summary>
    /// Optional thread where this memory was created.
    /// </summary>
    public int? SourceThreadId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public Thread? SourceThread { get; set; }
}
