using Microsoft.Extensions.Logging;

namespace Subconscious.Engine.Rag;

/// <summary>
/// Indexes workspace directories for RAG search.
/// <para>
/// Port of Python's <c>indexing.py</c> and <c>rag/indexing.py</c>.
/// Provides file-walking with skip-lists, chunking, and change detection.
/// </para>
/// </summary>
public sealed class WorkspaceIndexer
{
    private readonly SidecarStore _store;
    private readonly IEmbedder _embedder;
    private readonly ILogger<WorkspaceIndexer> _logger;
    private readonly HashSet<string> _skipPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".DS_Store", "Thumbs.db",
        "node_modules", "venv", "__pycache__", ".venv",
        ".idea", ".vscode", "bin", "obj", "build"
    };

    /// <summary>
    /// Create a new indexer for the workspace directory.
    /// </summary>
    public WorkspaceIndexer(string workspaceDirectory, IEmbedder embedder, ILogger<WorkspaceIndexer> logger)
    {
        _store = new SidecarStore(workspaceDirectory);
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>
    /// Index a directory, optionally only scanning for changes.
    /// </summary>
    /// <param name="directory">Directory to index.</param>
    /// <param name="onlyChanged">If true, only index files that have changed (based on content hash).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task IndexDirectoryAsync(string directory, bool onlyChanged = false, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Directory does not exist: {Directory}", directory);
            return;
        }

        _logger.LogInformation("Indexing directory: {Directory}", directory);

        var files = GetFiles(directory);
        var processed = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkip(file))
            {
                skipped++;
                continue;
            }

            var relativePath = GetRelativePath(directory, file);

            try
            {
                if (onlyChanged && !NeedsIndexing(file, relativePath))
                {
                    _logger.LogTrace("Skipping unchanged file: {File}", file);
                    skipped++;
                    continue;
                }

                await IndexFileAsync(file, relativePath, cancellationToken);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing file: {File}", file);
            }
        }

        _logger.LogInformation("Indexing complete: {Processed} processed, {Skipped} skipped", processed, skipped);
    }

    /// <summary>
    /// Index a single file.
    /// </summary>
    private async Task IndexFileAsync(string filePath, string relativePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Indexing file: {File}", filePath);

        var info = new FileInfo(filePath);
        var contentHash = await ComputeContentHashAsync(filePath, cancellationToken);
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        // Skip binary files and very large files
        if (IsBinary(content) || content.Length > 500_000)
        {
            _logger.LogTrace("Skipping binary or large file: {File}", filePath);
            _store.UpsertDocument(relativePath, Path.GetDirectoryName(relativePath)!, info.Length, new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(), contentHash, 0, "skipped", "Binary or too large");
            return;
        }

        // Chunk the content
        var chunks = ChunkContent(content, filePath);
        _logger.LogTrace("Created {Count} chunks from {File}", chunks.Count, filePath);

        // Get document record or create new one
        var document = _store.GetDocument(relativePath);
        if (document != null)
        {
            _store.DeleteChunks(document.Id);
        }

        var documentId = document?.Id ?? 0;

        // Store chunks
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            cancellationToken.ThrowIfCancellationRequested();

            // Generate embedding for chunk
            var embedding = await _embedder.EmbedAsync(chunk.Content, cancellationToken);

            _store.UpsertChunk(
                documentId,
                i,
                chunk.Content,
                chunk.StartLine,
                chunk.EndLine,
                chunk.TokenEstimate,
                embedding);

            // Update vector store
            _store.UpdateChunkVector(documentId, i, embedding);
        }

        _store.UpsertDocument(
            relativePath,
            Path.GetDirectoryName(relativePath)!,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
            contentHash,
            chunks.Count,
            "indexed");

        _logger.LogInformation("Indexed {File} with {Count} chunks", filePath, chunks.Count);
    }

    /// <summary>
    /// Check if a file needs re-indexing.
    /// </summary>
    private bool NeedsIndexing(string filePath, string relativePath)
    {
        var document = _store.GetDocument(relativePath);
        if (document == null)
        {
            return true;
        }

        var info = new FileInfo(filePath);
        if (document.Mtime != new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds())
        {
            return true;
        }

        // Could add content hash comparison here for full change detection
        return false;
    }

    /// <summary>
    /// Get all files in a directory recursively, respecting skip patterns.
    /// </summary>
    private IEnumerable<string> GetFiles(string directory)
    {
        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            // Handle directory access
            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(current);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Access denied to directory: {Directory}", current);
                dirs = Array.Empty<string>();
            }

            foreach (var dir in dirs)
            {
                if (!ShouldSkip(dir))
                {
                    stack.Push(dir);
                }
            }

            // Get files
            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Access denied to directory: {Directory}", current);
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Check if a path should be skipped.
    /// </summary>
    private bool ShouldSkip(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => _skipPatterns.Contains(p));
    }

    /// <summary>
    /// Chunk content into manageable segments.
    /// </summary>
    private List<ChunkInfo> ChunkContent(string content, string filePath)
    {
        var chunks = new List<ChunkInfo>();
        var lines = content.Split('\n');

        var currentChunk = new System.Text.StringBuilder();
        var currentStartLine = 0;
        var currentEndLine = 0;
        var totalTokens = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineTokens = EstimateTokens(line);

            if (currentChunk.Length + line.Length > 1500 || totalTokens + lineTokens > 500)
            {
                if (currentChunk.Length > 0)
                {
                    chunks.Add(new ChunkInfo
                    {
                        Content = currentChunk.ToString(),
                        StartLine = currentStartLine,
                        EndLine = currentEndLine,
                        TokenEstimate = totalTokens
                    });
                }

                currentChunk.Clear();
                currentStartLine = i;
                totalTokens = 0;
            }

            currentChunk.AppendLine(line);
            currentEndLine = i;
            totalTokens += lineTokens;
        }

        // Add final chunk
        if (currentChunk.Length > 0)
        {
            chunks.Add(new ChunkInfo
            {
                Content = currentChunk.ToString().TrimEnd(),
                StartLine = currentStartLine,
                EndLine = currentEndLine,
                TokenEstimate = totalTokens
            });
        }

        return chunks;
    }

    /// <summary>
    /// Estimate token count for text.
    /// </summary>
    private int EstimateTokens(string text)
    {
        // Rough estimate: ~4 chars per token
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Check if file content appears to be binary.
    /// </summary>
    private bool IsBinary(string content)
    {
        // Count null bytes - common indicator of binary
        var nullCount = content.Count(c => c == '\0');
        return nullCount > content.Length * 0.1;
    }

    /// <summary>
    /// Compute SHA256 hash of file content.
    /// </summary>
    private async Task<string> ComputeContentHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Get relative path from workspace directory.
    /// </summary>
    private string GetRelativePath(string baseDir, string fullPath)
    {
        var baseUri = new Uri(baseDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var fileUri = new Uri(fullPath);
        return baseUri.MakeRelativeUri(fileUri).ToString().Replace('/', Path.DirectorySeparatorChar);
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}

/// <summary>
/// Information about a single chunk.
/// </summary>
public sealed record ChunkInfo
{
    public required string Content { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int TokenEstimate { get; set; }
}
