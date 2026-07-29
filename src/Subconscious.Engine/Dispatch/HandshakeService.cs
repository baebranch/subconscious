using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Subconscious.Engine.Dispatch;

/// <summary>
/// Manages client handshakes and generates unique client IDs.
/// <para>
/// Port of Python's <c>dispatch/handshake.py</c>.
/// Ensures each connected client gets a unique ID and tracks connection metadata.
/// </para>
/// </summary>
public sealed class HandshakeService
{
    private readonly ConcurrentDictionary<string, ClientInfo> _clients = new();
    private readonly ConcurrentDictionary<string, string> _sessionIdToClientId = new();
    private readonly object _lock = new();

    /// <summary>
    /// Generate a unique client ID.
    /// </summary>
    public string GenerateClientId()
    {
        // Use UUID v4 for uniqueness
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Record a client handshake.
    /// </summary>
    /// <param name="clientId">The client's unique ID.</param>
    /// <param name="sessionId">The connection session ID.</param>
    /// <param name="metadata">Client metadata from the hello message.</param>
    public void RecordHandshake(string clientId, string sessionId, ClientHelloMetadata metadata)
    {
        var client = new ClientInfo
        {
            ClientId = clientId,
            SessionId = sessionId,
            Metadata = metadata,
            ConnectedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _clients[clientId] = client;
        _sessionIdToClientId[sessionId] = clientId;
    }

    /// <summary>
    /// Get client info by client ID.
    /// </summary>
    public bool TryGetClient(string clientId, out ClientInfo? client)
    {
        return _clients.TryGetValue(clientId, out client);
    }

    /// <summary>
    /// Update client last seen time.
    /// </summary>
    public void UpdateLastSeen(string clientId)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            client.LastSeenAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Remove a client on disconnect.
    /// </summary>
    public bool RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            // Remove session mapping
            _sessionIdToClientId.TryRemove(client.SessionId, out _);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Get all connected clients.
    /// </summary>
    public IReadOnlyList<ClientInfo> GetAllClients() => _clients.Values.ToList();

    /// <summary>
    /// Get the count of connected clients.
    /// </summary>
    public int ClientCount => _clients.Count;
}

/// <summary>
/// Information about a connected client.
/// </summary>
public sealed record ClientInfo
{
    /// <summary>
    /// Unique client ID.
    /// </summary>
    public required string ClientId { get; set; }

    /// <summary>
    /// Connection session ID.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Client metadata from hello message.
    /// </summary>
    public required ClientHelloMetadata Metadata { get; set; }

    /// <summary>
    /// When the client connected.
    /// </summary>
    public DateTime ConnectedAt { get; set; }

    /// <summary>
    /// When the client was last seen.
    /// </summary>
    public DateTime LastSeenAt { get; set; }
}

/// <summary>
/// Metadata from client hello message.
/// </summary>
public sealed record ClientHelloMetadata
{
    /// <summary>
    /// Client name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Client version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Client type (e.g., "code", "desktop", "web").
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Client capabilities.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; set; } = new List<string>();

    /// <summary>
    /// Profile root for tool routing.
    /// </summary>
    public string? ProfileRoot { get; set; }

    /// <summary>
    /// Additional client-specific data.
    /// </summary>
    public JsonNode? Extra { get; set; }
}
