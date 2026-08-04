namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// A chunk of an indexed document — the retrievable unit for RAG. The
/// ``Embedding`` column is reserved for vector search (JSON-encoded
/// vector or external index reference); it is unused by the current keyword
/// retrieval path.
/// </summary>
public class DocumentChunk
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int WorkspaceId { get; set; }
    /// <summary>
    /// Position within the document
    /// </summary>
    public int Ordinal { get; set; }
    public required string Content { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public int? TokenEstimate { get; set; }
    /// <summary>
    /// Vector store hook for future vector search implementation
    /// </summary>
    public string? Embedding { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public IndexedDocument? Document { get; set; }
    public Workspace? Workspace { get; set; }
}
