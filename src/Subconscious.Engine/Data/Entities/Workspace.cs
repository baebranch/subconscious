namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// A workspace within a network - contains threads, todos, memories, notes, contacts.
/// </summary>
public class Workspace
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int NetworkId { get; set; }
    public required string Uuid { get; set; }
    public string? ToolsConfig { get; set; }
    public string? SkillsConfig { get; set; }
    /// <summary>
    /// JSON list of absolute directory paths attached to the workspace for RAG indexing.
    /// </summary>
    public string? Directories { get; set; }
    /// <summary>
    /// JSON {\"query\": bool, \"mutation\": bool} — HITL approval policy for this workspace.
    /// </summary>
    public string? ApprovalConfig { get; set; }
    /// <summary>
    /// JSON {\"semantic_graph\": bool} — RAG/indexing options.
    /// </summary>
    public string? RagConfig { get; set; }
    /// <summary>
    /// Default model id for NEW threads in this workspace. NULL / \"default\" means
    /// \"use the first available model config\".
    /// </summary>
    public string? DefaultModelId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Network? Network { get; set; }
    public ICollection<Thread> Threads { get; set; } = new List<Thread>();
    public ICollection<TodoItem> TodoItems { get; set; } = new List<TodoItem>();
    public ICollection<WorkspaceMemory> Memories { get; set; } = new List<WorkspaceMemory>();
    public ICollection<Note> Notes { get; set; } = new List<Note>();
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<IndexedDocument> IndexedDocuments { get; set; } = new List<IndexedDocument>();
}
