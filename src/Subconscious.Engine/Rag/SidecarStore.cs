using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Subconscious.Engine.Rag;

/// <summary>
/// SQLite-based sidecar store for RAG indexing data.
/// <para>
/// Port of Python's <c>rag/sidecar.py</c>. Uses raw ADO.NET SQLite API to maintain
/// exact schema compatibility with the Python implementation, including the
/// same table names, column names, and data types.
/// </para>
/// </summary>
public sealed class SidecarStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _databasePath;

    /// <summary>
    /// Create or open a sidecar store at the specified path.
    /// </summary>
    /// <param name="workspaceDirectory">The directory being indexed (used to derive store path).</param>
    public SidecarStore(string workspaceDirectory)
    {
        _databasePath = Path.Combine(workspaceDirectory, ".subconscious", "sidecar.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();

        InitializeSchema();
    }

    /// <summary>
    /// Open an existing sidecar store.
    /// </summary>
    /// <param name="databasePath">Path to the existing sidecar database.</param>
    public SidecarStore(string databasePath, bool shouldCreate)
    {
        _databasePath = databasePath;

        if (shouldCreate && !File.Exists(_databasePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        }

        _connection = new SqliteConnection($"Data Source={_databasePath}");
        _connection.Open();

        if (shouldCreate)
        {
            InitializeSchema();
        }
    }

    private void InitializeSchema()
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        // Documents table - tracks indexed files
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL,
                directory TEXT,
                size INTEGER,
                mtime INTEGER,
                content_hash TEXT,
                chunk_count INTEGER DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'indexed',
                error TEXT,
                indexed_at INTEGER
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_documents_path ON documents(path);
            CREATE INDEX IF NOT EXISTS idx_documents_directory ON documents(directory);
            CREATE INDEX IF NOT EXISTS idx_documents_status ON documents(status);
        ";
        command.ExecuteNonQuery();

        // Chunks table - document text segments
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                content TEXT NOT NULL,
                start_line INTEGER,
                end_line INTEGER,
                token_estimate INTEGER,
                embedding BLOB,
                created_at INTEGER NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_chunks_document ON chunks(document_id);
            CREATE INDEX IF NOT EXISTS idx_chunks_ordinal ON chunks(document_id, ordinal);
        ";
        command.ExecuteNonQuery();

        // Chunk vectors table - vector storage for chunks
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS chunk_vectors (
                chunk_id INTEGER PRIMARY KEY,
                vector BLOB NOT NULL,
                FOREIGN KEY (chunk_id) REFERENCES chunks(id) ON DELETE CASCADE
            );
        ";
        command.ExecuteNonQuery();

        // Knowledge graph nodes
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS kg_nodes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                node_id TEXT NOT NULL UNIQUE,
                label TEXT,
                properties TEXT,
                embedding BLOB,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_kg_nodes_label ON kg_nodes(label);
        ";
        command.ExecuteNonQuery();

        // Knowledge graph edges
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS kg_edges (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                from_node_id TEXT NOT NULL,
                to_node_id TEXT NOT NULL,
                relationship TEXT NOT NULL,
                properties TEXT,
                created_at INTEGER NOT NULL,
                FOREIGN KEY (from_node_id) REFERENCES kg_nodes(node_id),
                FOREIGN KEY (to_node_id) REFERENCES kg_nodes(node_id)
            );
            CREATE INDEX IF NOT EXISTS idx_kg_edges_from ON kg_edges(from_node_id);
            CREATE INDEX IF NOT EXISTS idx_kg_edges_to ON kg_edges(to_node_id);
            CREATE INDEX IF NOT EXISTS idx_kg_edges_rel ON kg_edges(relationship);
        ";
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    #region Document operations

    /// <summary>
    /// Add or update a document record.
    /// </summary>
    public void UpsertDocument(string path, string directory, long size, long mtime, string? contentHash, int chunkCount, string status, string? error = null)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        command.CommandText = @"
            INSERT INTO documents (path, directory, size, mtime, content_hash, chunk_count, status, error, indexed_at)
            VALUES (@path, @directory, @size, @mtime, @content_hash, @chunk_count, @status, @error, @indexed_at)
            ON CONFLICT(path) DO UPDATE SET
                directory = @directory,
                size = @size,
                mtime = @mtime,
                content_hash = @content_hash,
                chunk_count = @chunk_count,
                status = @status,
                error = @error,
                indexed_at = @indexed_at;
        ";

        command.Parameters.AddWithValue("@path", path);
        command.Parameters.AddWithValue("@directory", directory);
        command.Parameters.AddWithValue("@size", size);
        command.Parameters.AddWithValue("@mtime", mtime);
        command.Parameters.AddWithValue("@content_hash", contentHash ?? "");
        command.Parameters.AddWithValue("@chunk_count", chunkCount);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@error", error ?? "");
        command.Parameters.AddWithValue("@indexed_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>
    /// Get a document by path.
    /// </summary>
    public Document? GetDocument(string path)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM documents WHERE path = @path";
        command.Parameters.AddWithValue("@path", path);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new Document
            {
                Id = reader.GetInt32(0),
                Path = reader.GetString(1),
                Directory = reader.IsDBNull(2) ? null : reader.GetString(2),
                Size = reader.GetInt64(3),
                Mtime = reader.GetInt64(4),
                ContentHash = reader.IsDBNull(5) ? null : reader.GetString(5),
                ChunkCount = reader.GetInt32(6),
                Status = reader.GetString(7),
                Error = reader.IsDBNull(8) ? null : reader.GetString(8),
                IndexedAt = reader.GetInt64(9)
            };
        }

        return null;
    }

    /// <summary>
    /// List all documents.
    /// </summary>
    public IReadOnlyList<Document> ListDocuments()
    {
        var documents = new List<Document>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM documents ORDER BY path";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            documents.Add(new Document
            {
                Id = reader.GetInt32(0),
                Path = reader.GetString(1),
                Directory = reader.IsDBNull(2) ? null : reader.GetString(2),
                Size = reader.GetInt64(3),
                Mtime = reader.GetInt64(4),
                ContentHash = reader.IsDBNull(5) ? null : reader.GetString(5),
                ChunkCount = reader.GetInt32(6),
                Status = reader.GetString(7),
                Error = reader.IsDBNull(8) ? null : reader.GetString(8),
                IndexedAt = reader.GetInt64(9)
            });
        }

        return documents;
    }

    /// <summary>
    /// Delete a document and its chunks.
    /// </summary>
    public void DeleteDocument(string path)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        command.CommandText = "DELETE FROM documents WHERE path = @path";
        command.Parameters.AddWithValue("@path", path);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    #endregion

    #region Chunk operations

    /// <summary>
    /// Add or update a chunk.
    /// </summary>
    public void UpsertChunk(int documentId, int ordinal, string content, int? startLine, int? endLine, int? tokenEstimate, double[]? embedding = null)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        // Insert or update chunk
        command.CommandText = @"
            INSERT INTO chunks (document_id, ordinal, content, start_line, end_line, token_estimate, created_at)
            VALUES (@document_id, @ordinal, @content, @start_line, @end_line, @token_estimate, @created_at)
            ON CONFLICT(document_id, ordinal) DO UPDATE SET
                content = @content,
                start_line = @start_line,
                end_line = @end_line,
                token_estimate = @token_estimate;
        ";

        command.Parameters.AddWithValue("@document_id", documentId);
        command.Parameters.AddWithValue("@ordinal", ordinal);
        command.Parameters.AddWithValue("@content", content);
        command.Parameters.AddWithValue("@start_line", startLine.HasValue ? startLine.Value : DBNull.Value);
        command.Parameters.AddWithValue("@end_line", endLine.HasValue ? endLine.Value : DBNull.Value);
        command.Parameters.AddWithValue("@token_estimate", tokenEstimate.HasValue ? tokenEstimate.Value : DBNull.Value);
        command.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        command.ExecuteNonQuery();

        // Update embedding if provided
        if (embedding != null)
        {
            UpdateChunkVector(documentId, ordinal, embedding);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Get chunks for a document.
    /// </summary>
    public IReadOnlyList<Chunk> GetChunks(int documentId)
    {
        var chunks = new List<Chunk>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM chunks WHERE document_id = @document_id ORDER BY ordinal";
        command.Parameters.AddWithValue("@document_id", documentId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            chunks.Add(new Chunk
            {
                Id = reader.GetInt32(0),
                DocumentId = reader.GetInt32(1),
                Ordinal = reader.GetInt32(2),
                Content = reader.GetString(3),
                StartLine = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                EndLine = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                TokenEstimate = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                CreatedAt = reader.GetInt64(7)
            });
        }

        return chunks;
    }

    /// <summary>
    /// Delete all chunks for a document.
    /// </summary>
    public void DeleteChunks(int documentId)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        command.CommandText = "DELETE FROM chunks WHERE document_id = @document_id";
        command.Parameters.AddWithValue("@document_id", documentId);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    #endregion

    #region Vector operations

    /// <summary>
    /// Update a chunk's vector embedding.
    /// </summary>
    public void UpdateChunkVector(int documentId, int ordinal, double[] vector)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        // First get chunk ID
        command.CommandText = "SELECT id FROM chunks WHERE document_id = @document_id AND ordinal = @ordinal";
        command.Parameters.AddWithValue("@document_id", documentId);
        command.Parameters.AddWithValue("@ordinal", ordinal);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            transaction.Rollback();
            return;
        }

        var chunkId = reader.GetInt32(0);

        // Serialize vector to JSON for storage
        var json = JsonSerializer.Serialize(vector);

        command.CommandText = @"
            INSERT OR REPLACE INTO chunk_vectors (chunk_id, vector)
            VALUES (@chunk_id, @vector);
        ";

        command.Parameters.AddWithValue("@chunk_id", chunkId);
        command.Parameters.AddWithValue("@vector", json);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    /// <summary>
    /// Get a chunk's vector embedding.
    /// </summary>
    public double[]? GetChunkVector(int chunkId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT vector FROM chunk_vectors WHERE chunk_id = @chunk_id";
        command.Parameters.AddWithValue("@chunk_id", chunkId);

        var result = command.ExecuteScalar();
        if (result == null || result == DBNull.Value)
        {
            return null;
        }

        var json = result.ToString()!;
        return JsonSerializer.Deserialize<double[]>(json);
    }

    #endregion

    #region Knowledge graph operations

    /// <summary>
    /// Add or update a knowledge graph node.
    /// </summary>
    public void UpsertNode(string nodeId, string? label, string? properties, double[]? embedding = null)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        var jsonProps = properties ?? "{}";
        var jsonEmbedding = embedding != null ? JsonSerializer.Serialize(embedding) : null;

        command.CommandText = @"
            INSERT OR REPLACE INTO kg_nodes (node_id, label, properties, embedding, created_at)
            VALUES (@node_id, @label, @properties, @embedding, @created_at);
        ";

        command.Parameters.AddWithValue("@node_id", nodeId);
        command.Parameters.AddWithValue("@label", label ?? "");
        command.Parameters.AddWithValue("@properties", jsonProps);
        command.Parameters.AddWithValue("@embedding", string.IsNullOrEmpty(jsonEmbedding) ? DBNull.Value : jsonEmbedding);
        command.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>
    /// Add a knowledge graph edge.
    /// </summary>
    public void AddEdge(string fromNodeId, string toNodeId, string relationship, string? properties = null)
    {
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();

        var jsonProps = properties ?? "{}";

        command.CommandText = @"
            INSERT INTO kg_edges (from_node_id, to_node_id, relationship, properties, created_at)
            VALUES (@from_node_id, @to_node_id, @relationship, @properties, @created_at);
        ";

        command.Parameters.AddWithValue("@from_node_id", fromNodeId);
        command.Parameters.AddWithValue("@to_node_id", toNodeId);
        command.Parameters.AddWithValue("@relationship", relationship);
        command.Parameters.AddWithValue("@properties", jsonProps);
        command.Parameters.AddWithValue("@created_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        command.ExecuteNonQuery();
        transaction.Commit();
    }

    #endregion

    #region Search operations

    /// <summary>
    /// Search chunks by keyword match.
    /// </summary>
    public IReadOnlyList<SearchResult> KeywordSearch(string query, int limit = 20)
    {
        var results = new List<SearchResult>();
        using var command = _connection.CreateCommand();

        // Simple keyword search - exact substring match
        command.CommandText = @"
            SELECT c.*, d.path
            FROM chunks c
            JOIN documents d ON c.document_id = d.id
            WHERE c.content LIKE @query
            ORDER BY c.id
            LIMIT @limit;
        ";

        command.Parameters.AddWithValue("@query", "%" + query + "%");
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SearchResult
            {
                Chunk = new Chunk
                {
                    Id = reader.GetInt32(0),
                    DocumentId = reader.GetInt32(1),
                    Ordinal = reader.GetInt32(2),
                    Content = reader.GetString(3),
                    StartLine = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    EndLine = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    TokenEstimate = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    CreatedAt = reader.GetInt64(7)
                },
                DocumentPath = reader.GetString(8),
                Score = 1.0
            });
        }

        return results;
    }

    /// <summary>
    /// Search chunks by vector similarity.
    /// </summary>
    public IReadOnlyList<SearchResult> VectorSearch(double[] queryVector, int limit = 20, double minSimilarity = 0.5)
    {
        var results = new List<SearchResult>();
        var allChunks = GetChunksWithVectors();

        foreach (var chunk in allChunks)
        {
            if (chunk.Vector == null) continue;

            var similarity = ComputeCosineSimilarity(queryVector, chunk.Vector);
            if (similarity >= minSimilarity)
            {
                results.Add(new SearchResult
                {
                    Chunk = chunk.Chunk,
                    DocumentPath = chunk.DocumentPath,
                    Score = similarity
                });
            }
        }

        // Sort by similarity and limit
        return results.OrderByDescending(r => r.Score).Take(limit).ToList();
    }

    /// <summary>
    /// Hybrid search combining keyword and vector search.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> HybridSearchAsync(string query, IEmbedder embedder, int limit = 20)
    {
        // Perform keyword search
        var keywordResults = KeywordSearch(query, limit * 2);

        // Embed query and perform vector search
        var queryVector = await embedder.EmbedAsync(query);
        var vectorResults = VectorSearch(queryVector, limit * 2);

        // RRF (Reciprocal Rank Fusion) to combine results
        var combined = new Dictionary<string, SearchResult>();
        var rrfRank = 1;

        foreach (var result in keywordResults)
        {
            if (!combined.ContainsKey(result.Chunk.Id.ToString()))
            {
                combined[result.Chunk.Id.ToString()] = result;
            }
            combined[result.Chunk.Id.ToString()].Score += 1.0 / (rrfRank + 60);
            rrfRank++;
        }

        rrfRank = 1;
        foreach (var result in vectorResults)
        {
            var key = result.Chunk.Id.ToString();
            if (!combined.ContainsKey(key))
            {
                combined[key] = result;
            }
            combined[key].Score += 1.0 / (rrfRank + 60);
            rrfRank++;
        }

        return combined.Values.OrderByDescending(r => r.Score).Take(limit).ToList();
    }

    #endregion

    private IReadOnlyList<ChunkWithVector> GetChunksWithVectors()
    {
        var chunks = new List<ChunkWithVector>();
        using var command = _connection.CreateCommand();

        command.CommandText = @"
            SELECT c.*, d.path, cv.vector
            FROM chunks c
            JOIN documents d ON c.document_id = d.id
            LEFT JOIN chunk_vectors cv ON c.id = cv.chunk_id
            ORDER BY c.id;
        ";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var chunk = new Chunk
            {
                Id = reader.GetInt32(0),
                DocumentId = reader.GetInt32(1),
                Ordinal = reader.GetInt32(2),
                Content = reader.GetString(3),
                StartLine = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                EndLine = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                TokenEstimate = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                CreatedAt = reader.GetInt64(7)
            };

            var vector = reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<double[]>(reader.GetString(8));

            chunks.Add(new ChunkWithVector
            {
                Chunk = chunk,
                DocumentPath = reader.GetString(8),
                Vector = vector
            });
        }

        return chunks;
    }

    private static double ComputeCosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length) return 0.0;

        var dot = 0.0;
        var magA = 0.0;
        var magB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom > 0 ? dot / denom : 0.0;
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}

/// <summary>
/// Document record in the sidecar store.
/// </summary>
public sealed record Document
{
    public int Id { get; set; }
    public required string Path { get; set; }
    public string? Directory { get; set; }
    public long Size { get; set; }
    public long Mtime { get; set; }
    public string? ContentHash { get; set; }
    public int ChunkCount { get; set; }
    public required string Status { get; set; }
    public string? Error { get; set; }
    public long IndexedAt { get; set; }
}

/// <summary>
/// Chunk record in the sidecar store.
/// </summary>
public sealed record Chunk
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int Ordinal { get; set; }
    public required string Content { get; set; }
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
    public int? TokenEstimate { get; set; }
    public long CreatedAt { get; set; }
}

/// <summary>
/// Chunk with its vector embedding for similarity search.
/// </summary>
public sealed record ChunkWithVector
{
    public required Chunk Chunk { get; set; }
    public required string DocumentPath { get; set; }
    public double[]? Vector { get; set; }
}

/// <summary>
/// Search result combining chunk data with relevance score.
/// </summary>
public sealed record SearchResult
{
    public required Chunk Chunk { get; set; }
    public required string DocumentPath { get; set; }
    public double Score { get; set; }
}
