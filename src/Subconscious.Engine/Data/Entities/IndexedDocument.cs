namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// A file within a workspace's attached directories that has been ingested for
/// retrieval (RAG). Tracks size/mtime/hash so re-indexing can be incremental.
/// Status values: 'indexed', 'error'.
/// </summary>
public class IndexedDocument
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    /// <summary>
    /// Absolute file path
    /// </summary>
    public required string Path { get; set; }
    /// <summary>
    /// Attached root directory this file came from
    /// </summary>
    public string? Directory { get; set; }
    public int? Size { get; set; }
    /// <summary>
    /// int(st_mtime) for cheap change detection
    /// </summary>
    public int? Mtime { get; set; }
    /// <summary>
    /// sha256 for small files
    /// </summary>
    public string? ContentHash { get; set; }
    public int ChunkCount { get; set; }
    /// <summary>
    /// Status: indexed, error
    /// </summary>
    public required string Status { get; set; } = "indexed";
    public string? Error { get; set; }
    public DateTime IndexedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}
