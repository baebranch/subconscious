using System.Security.Cryptography;

namespace Subconscious.Engine.Rag;

/// <summary>
/// Abstraction for generating vector embeddings from text.
/// <para>
/// The Python implementation uses a pure-Python brute-force cosine implementation
/// with a custom hashing-based embedding approach. This port preserves that approach
/// rather than using a heavy ML library, maintaining compatibility with the existing
/// sidecar store schema.
/// </para>
/// </summary>
public interface IEmbedder
{
    /// <summary>
    /// Generate a dense embedding vector from text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dense vector embedding.</returns>
    Task<double[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a sparse embedding (hash-based) from text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="dimensions">Number of dimensions for the sparse vector.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A sparse embedding represented as a dictionary of index->value.</returns>
    Task<Dictionary<int, double>> EmbedSparseAsync(
        string text,
        int dimensions = 1024,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate cosine similarity between two vectors.
    /// </summary>
    double CosineSimilarity(double[] a, double[] b);
}

/// <summary>
/// Offline hashing embedder that generates deterministic embeddings without ML models.
/// Port of Python's <c>rag/embeddings.py</c> hashing-based approach.
/// </summary>
public sealed class HashingEmbedder : IEmbedder
{
    private const int DefaultDimensions = 768;

    /// <summary>
    /// Generate a dense embedding using hash-based feature extraction.
    /// <para>
    /// This approach:
    /// 1. Tokenizes text into n-grams
    /// 2. Hashes each n-gram to a fixed set of indices
    /// 3. Counts occurrences and applies TF-IDF weighting
    /// 4. Normalizes the resulting vector
    /// </para>
    /// </summary>
    public async Task<double[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
        {
            return new double[DefaultDimensions];
        }

        var tokens = Tokenize(text);
        var frequencies = new Dictionary<int, int>();

        // Generate n-grams and hash to indices
        foreach (var token in tokens)
        {
            for (int n = 1; n <= 3 && n <= token.Length; n++)
            {
                for (int i = 0; i <= token.Length - n; i++)
                {
                    var ngram = token.Substring(i, n);
                    var hash = HashString(ngram);
                    var index = hash % DefaultDimensions;
                    frequencies[index] = frequencies.GetValueOrDefault(index) + 1;
                }
            }
        }

        // Apply TF-IDF weighting and normalize
        var embedding = new double[DefaultDimensions];
        var totalTokens = tokens.Length;

        foreach (var (index, count) in frequencies)
        {
            var tf = (double)count / totalTokens;
            var idf = Math.Log(1 + DefaultDimensions / (frequencies.Count + 1.0));
            embedding[index] = tf * idf;
        }

        // L2 normalization
        var magnitude = Math.Sqrt(embedding.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }

        return embedding;
    }

    /// <summary>
    /// Generate a sparse embedding with hashed features.
    /// </summary>
    public async Task<Dictionary<int, double>> EmbedSparseAsync(
        string text,
        int dimensions = 1024,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
        {
            return new Dictionary<int, double>();
        }

        var tokens = Tokenize(text);
        var frequencies = new Dictionary<int, int>();

        foreach (var token in tokens)
        {
            for (int n = 1; n <= 3 && n <= token.Length; n++)
            {
                for (int i = 0; i <= token.Length - n; i++)
                {
                    var ngram = token.Substring(i, n);
                    var hash = HashString(ngram);
                    var index = hash % dimensions;
                    frequencies[index] = frequencies.GetValueOrDefault(index) + 1;
                }
            }
        }

        // Convert to sparse representation
        var sparse = new Dictionary<int, double>();
        var totalTokens = tokens.Length;

        foreach (var (index, count) in frequencies)
        {
            var tf = (double)count / totalTokens;
            sparse[index] = tf;
        }

        return sparse;
    }

    /// <summary>
    /// Calculate cosine similarity between two vectors.
    /// </summary>
    public double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors must have the same dimensionality");
        }

        var dotProduct = 0.0;
        var magnitudeA = 0.0;
        var magnitudeB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);
        return denom > 0 ? dotProduct / denom : 0.0;
    }

    /// <summary>
    /// Tokenize text into words, lowercasing and removing non-alphanumeric characters.
    /// </summary>
    private static string[] Tokenize(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words.ToArray();
    }

    /// <summary>
    /// Hash a string to a non-negative integer using SHA256.
    /// </summary>
    private static int HashString(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        // Use first 4 bytes for 32-bit hash
        return Math.Abs(BitConverter.ToInt32(hash, 0));
    }
}
