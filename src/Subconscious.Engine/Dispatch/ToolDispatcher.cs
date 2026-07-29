using System.Text.Json;
using System.Text.Json.Nodes;

namespace Subconscious.Engine.Dispatch;

/// <summary>
/// Routes tool calls to appropriate providers based on tool ID and routing rules.
/// <para>
/// Port of Python's <c>dispatch/dispatcher.py</c>.
/// Handles tool resolution, routing, and result aggregation.
/// </para>
/// </summary>
public sealed class ToolDispatcher
{
    private readonly ProviderTable _providerTable;
    private readonly object _lock = new();

    /// <summary>
    /// Create a new ToolDispatcher.
    /// </summary>
    /// <param name="providerTable">The provider table to use for tool resolution.</param>
    public ToolDispatcher(ProviderTable providerTable)
    {
        _providerTable = providerTable;
    }

    /// <summary>
    /// Dispatch a tool call to the appropriate provider.
    /// </summary>
    /// <param name="toolId">Fully-qualified tool ID to call.</param>
    /// <param name="input">Tool arguments as JSON.</param>
    /// <param name="profileRoot">Profile root to prefer for routing.</param>
    /// <param name="routingKey">Routing key for client tools.</param>
    /// <param name="correlationId">Unique ID to correlate request/response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool result as JSON.</returns>
    public async Task<JsonNode?> DispatchAsync(
        string toolId,
        JsonNode? input,
        string? profileRoot = null,
        string? routingKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedToolId = ResolveToolId(toolId, profileRoot);

        var provider = _providerTable.ResolveProvider(resolvedToolId, profileRoot, routingKey);

        if (provider == null)
        {
            throw new NoProviderException($"No provider found for tool '{resolvedToolId}'");
        }

        // Use the provided correlation ID or generate a new one
        var cid = correlationId ?? Guid.NewGuid().ToString("N");

        return await provider.SendToolCallAsync(cid, resolvedToolId, input, cancellationToken);
    }

    /// <summary>
    /// Resolve a tool ID to its fully-qualified form.
    /// </summary>
    /// <param name="toolId">Tool ID (may be prefixed).</param>
    /// <param name="profileRoot">Profile root to use if not already prefixed.</param>
    private string ResolveToolId(string toolId, string? profileRoot)
    {
        // If already fully qualified (contains colon), return as-is
        if (toolId.Contains(':'))
        {
            return toolId;
        }

        // If no profile root provided, use default
        if (string.IsNullOrEmpty(profileRoot))
        {
            return $"default:{toolId}";
        }

        return $"{profileRoot}:{toolId}";
    }

    /// <summary>
    /// Check if a tool is available from any provider.
    /// </summary>
    /// <param name="toolId">Tool ID to check.</param>
    /// <param name="profileRoot">Optional profile root to restrict search.</param>
    public bool IsToolAvailable(string toolId, string? profileRoot = null)
    {
        var resolvedToolId = ResolveToolId(toolId, profileRoot);
        return _providerTable.FindProviders(resolvedToolId, profileRoot).Count > 0;
    }

    /// <summary>
    /// Register tools from a provider.
    /// </summary>
    /// <param name="provider">The provider registering tools.</param>
    /// <param name="tools">List of tools to register.</param>
    public void RegisterTools(Provider provider, IReadOnlyList<ToolRegistration> tools)
    {
        provider.RegisterTools(tools);
        _providerTable.Add(provider);
    }

    /// <summary>
    /// Unregister tools from a provider.
    /// </summary>
    /// <param name="provider">The provider unregistering tools.</param>
    /// <param name="toolIds">List of tool IDs to unregister.</param>
    public void UnregisterTools(Provider provider, IReadOnlyList<string> toolIds)
    {
        provider.UnregisterTools(toolIds);

        // If provider has no more tools, remove it
        if (!provider.ToolIds.Any())
        {
            _providerTable.Remove(provider.ProviderId);
        }
    }

    /// <summary>
    /// Get all registered tools across all providers.
    /// </summary>
    public IReadOnlyList<ProviderTool> GetAllTools() => _providerTable.GetAllTools();
}

/// <summary>
/// Exception thrown when no provider is found for a tool.
/// </summary>
public sealed class NoProviderException : Exception
{
    public NoProviderException(string message) : base(message) { }
}

/// <summary>
/// Exception thrown when tool dispatch fails.
/// </summary>
public sealed class ToolDispatchException : Exception
{
    public string? ProviderId { get; }

    public ToolDispatchException(string message, string? providerId = null)
        : base(message)
    {
        ProviderId = providerId;
    }

    public ToolDispatchException(string message, Exception innerException, string? providerId = null)
        : base(message, innerException)
    {
        ProviderId = providerId;
    }
}
