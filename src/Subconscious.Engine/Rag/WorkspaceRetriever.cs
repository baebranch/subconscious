using Microsoft.Extensions.Logging;

namespace Subconscious.Engine.Rag;

/// <summary>
/// Retrieves and ranks indexed content for search queries.
/// <para>
/// Port of Python's <c>rag/retrieval.py</c>.
/// Supports keyword search, vector search, and hybrid search.
/// </para>
/// </summary>
public sealed class WorkspaceRetriever : IDisposable
{
    private readonly SidecarStore _store;
    private readonly IEmbedder _embedder;
    private readonly ILogger<WorkspaceRetriever> _logger;

    /// <summary>
    /// Create a new retriever for the workspace directory.
    /// </summary>
    public WorkspaceRetriever(string workspaceDirectory, IEmbedder embedder, ILogger<WorkspaceRetriever> logger)
    {
        _store = new SidecarStore(Path.Combine(workspaceDirectory, ".subconscious", "sidecar.db"), shouldCreate: false);
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>
    /// Search the workspace using keyword, vector, or hybrid search.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="searchType">Type of search to perform.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="minSimilarity">Minimum similarity score for vector search.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        SearchType searchType = SearchType.Hybrid,
        int limit = 20,
        double minSimilarity = 0.5,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        _logger.LogInformation("Searching with query: {Query}, Type: {Type}", query, searchType);

        switch (searchType)
        {
            case SearchType.Keyword:
                return _store.KeywordSearch(query, limit);

            case SearchType.Vector:
            {
                var queryVector = await _embedder.EmbedAsync(query, cancellationToken);
                return _store.VectorSearch(queryVector, limit, minSimilarity);
            }

            case SearchType.Hybrid:
                return await _store.HybridSearchAsync(query, _embedder, limit);

            default:
                return [];
        }
    }

    /// <summary>
    /// Search the knowledge graph for related concepts.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeGraphResult>> GraphSearchAsync(
        string query,
        int depth = 2,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        _logger.LogInformation("Graph searching with query: {Query}, Depth: {Depth}", query, depth);

        // First, find seed nodes via keyword search on node properties
        var seedNodes = FindSeedNodes(query, limit);

        if (seedNodes.Count == 0)
        {
            // Fallback: search chunks and extract related nodes
            var chunkResults = _store.KeywordSearch(query, limit);
            var result = new List<KnowledgeGraphResult>();

            foreach (var chunkResult in chunkResults)
            {
                result.Add(new KnowledgeGraphResult
                {
                    Type = SearchResultType.Chunk,
                    Chunk = chunkResult.Chunk,
                    DocumentPath = chunkResult.DocumentPath,
                    Score = chunkResult.Score,
                    RelatedNodes = new List<GraphNode>()
                });
            }

            return result;
        }

        // Expand graph from seed nodes
        var results = new List<KnowledgeGraphResult>();

        foreach (var seed in seedNodes)
        {
            var expanded = ExpandGraph(seed, depth, limit);
            results.Add(new KnowledgeGraphResult
            {
                Type = SearchResultType.Node,
                Node = seed,
                Score = 1.0,
                RelatedNodes = expanded
            });
        }

        return results.OrderByDescending(r => r.Score).Take(limit).ToList();
    }

    /// <summary>
    /// Find seed nodes matching the query.
    /// </summary>
    private List<GraphNode> FindSeedNodes(string query, int limit)
    {
        // In a full implementation, this would use vector similarity on node embeddings
        // For now, we'll do a simple placeholder that returns empty
        return new List<GraphNode>();
    }

    /// <summary>
    /// Expand graph from a seed node by following edges.
    /// </summary>
    private List<GraphNode> ExpandGraph(GraphNode seed, int depth, int limit)
    {
        // In a full implementation, this would traverse the knowledge graph
        // For now, return empty list
        return new List<GraphNode>();
    }

    /// <summary>
    /// Get chunks for a specific document.
    /// </summary>
    public IReadOnlyList<Chunk> GetDocumentChunks(string documentPath)
    {
        var document = _store.GetDocument(documentPath);
        if (document == null)
        {
            return [];
        }

        return _store.GetChunks(document.Id);
    }

    /// <summary>
    /// List all indexed documents.
    /// </summary>
    public IReadOnlyList<Document> ListDocuments()
    {
        return _store.ListDocuments();
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}

/// <summary>
/// Type of search result.
/// </summary>
public enum SearchResultType
{
    Chunk,
    Node,
    Relation
}

/// <summary>
/// Result from knowledge graph search.
/// </summary>
public sealed record KnowledgeGraphResult
{
    public SearchResultType Type { get; set; }
    public Chunk? Chunk { get; set; }
    public string? DocumentPath { get; set; }
    public GraphNode? Node { get; set; }
    public double Score { get; set; }
    public required List<GraphNode> RelatedNodes { get; set; }
}

/// <summary>
/// Node in the knowledge graph.
/// </summary>
public sealed record GraphNode
{
    public required string NodeId { get; set; }
    public string? Label { get; set; }
    public string? Properties { get; set; }
    public required List<GraphRelation> Relations { get; set; }
}

/// <summary>
/// Relationship between graph nodes.
/// </summary>
public sealed record GraphRelation
{
    public required string ToNodeId { get; set; }
    public required string Relationship { get; set; }
    public string? Properties { get; set; }
}

/// <summary>
/// Type of search to perform.
/// </summary>
public enum SearchType
{
    Keyword,
    Vector,
    Hybrid
}
