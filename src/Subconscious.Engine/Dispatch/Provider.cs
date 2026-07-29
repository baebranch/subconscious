using System.Text.Json;
using System.Text.Json.Nodes;

namespace Subconscious.Engine.Dispatch;

/// <summary>
/// Represents a connected provider (client or engine) that can receive tool calls.
/// <para>
/// Port of Python's <c>dispatch/provider.py</c>. Mirrors the <c>Provider</c> class which
/// wraps a <c>ProviderConnection</c> and tracks metadata like registered tools and permissions.
/// </para>
/// </summary>
public sealed class Provider
{
    private readonly ProviderConnection _connection;
    private readonly HashSet<string> _toolIds = new();
    private readonly object _lock = new();

    /// <summary>
    /// Unique identifier for this provider.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Client ID if this is a client connection (from HandshakeService).
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Connection metadata.
    /// </summary>
    public ProviderMetadata Metadata { get; }

    /// <summary>
    /// Profile root used for tool routing.
    /// </summary>
    public string ProfileRoot { get; }

    /// <summary>
    /// Tools registered by this provider, keyed by fully-qualified tool ID.
    /// </summary>
    public IReadOnlySet<string> ToolIds => _toolIds;

    /// <summary>
    /// Create a new Provider.
    /// </summary>
    /// <param name="connection">The underlying connection to send tool calls to.</param>
    /// <param name="providerId">Unique provider ID.</param>
    /// <param name="metadata">Provider metadata.</param>
    /// <param name="profileRoot">Profile root for tool routing.</param>
    public Provider(ProviderConnection connection, string providerId, ProviderMetadata metadata, string profileRoot)
    {
        _connection = connection;
        ProviderId = providerId;
        Metadata = metadata;
        ProfileRoot = profileRoot;
    }

    /// <summary>
    /// Register tools with this provider.
    /// </summary>
    /// <param name="tools">List of tool definitions.</param>
    public void RegisterTools(IReadOnlyList<ToolRegistration> tools)
    {
        lock (_lock)
        {
            foreach (var tool in tools)
            {
                _toolIds.Add(tool.Id);
            }
        }
    }

    /// <summary>
    /// Unregister tools from this provider.
    /// </summary>
    /// <param name="toolIds">List of tool IDs to unregister.</param>
    public void UnregisterTools(IReadOnlyList<string> toolIds)
    {
        lock (_lock)
        {
            foreach (var toolId in toolIds)
            {
                _toolIds.RemoveWhere(id => id == toolId || id.StartsWith(toolId + "."));
            }
        }
    }

    /// <summary>
    /// Send a tool call to this provider.
    /// </summary>
    /// <param name="correlationId">Unique ID to correlate request/response.</param>
    /// <param name="toolId">Fully-qualified tool ID.</param>
    /// <param name="input">Tool arguments as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<JsonNode?> SendToolCallAsync(string correlationId, string toolId, JsonNode? input, CancellationToken cancellationToken = default)
    {
        await _connection.SendToolCall(correlationId, toolId, input, cancellationToken);
        return null; // Tool call results are typically returned asynchronously via a callback
    }

    /// <summary>
    /// Close the provider connection.
    /// </summary>
    public async ValueTask CloseAsync()
    {
        if (_connection is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _connection.Dispose();
        }
    }

    /// <summary>
    /// Check if this provider has a specific tool registered.
    /// </summary>
    public bool HasTool(string toolId)
    {
        lock (_lock)
        {
            return _toolIds.Contains(toolId) || _toolIds.Any(id => id.StartsWith(toolId + "."));
        }
    }
}

/// <summary>
/// Metadata about a provider.
/// </summary>
public sealed record ProviderMetadata
{
    /// <summary>
    /// Human-readable name of the provider.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Provider version.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Provider type (client, engine, etc.).
    /// </summary>
    public string Type { get; init; } = "client";

    /// <summary>
    /// Additional metadata as JSON.
    /// </summary>
    public JsonNode? Extra { get; init; }
}

/// <summary>
/// Tool registration request.
/// </summary>
public sealed record ToolRegistration
{
    /// <summary>
    /// Fully-qualified tool ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Tool name (without provider prefix).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Tool description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Tool schema.
    /// </summary>
    public JsonNode? Schema { get; init; }
}

/// <summary>
/// Connection interface for sending tool calls to a provider.
/// <para>
/// This is the abstraction that all provider connections must implement.
/// Port of Python's <c>dispatch/provider.py</c>'s <c>ProviderConnection</c>.
/// </para>
/// </summary>
public interface ProviderConnection : IDisposable
{
    /// <summary>
    /// Send a tool call to the provider.
    /// </summary>
    /// <param name="correlationId">Unique ID to correlate request/response.</param>
    /// <param name="toolId">Fully-qualified tool ID.</param>
    /// <param name="input">Tool arguments as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendToolCall(string correlationId, string toolId, JsonNode? input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a tool call to the provider and await the result.
    /// </summary>
    /// <param name="correlationId">Unique ID to correlate request/response.</param>
    /// <param name="toolId">Fully-qualified tool ID.</param>
    /// <param name="input">Tool arguments as JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tool result as JSON, or null if not available.</returns>
    Task<JsonNode?> SendToolCallAsync(string correlationId, string toolId, JsonNode? input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Disposable async version of ProviderConnection.
/// </summary>
public interface IAsyncProviderConnection : ProviderConnection, IAsyncDisposable
{
}
