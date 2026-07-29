using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Subconscious.Engine.Dispatch;

/// <summary>
/// Thread-safe registry of all connected providers.
/// <para>
/// Port of Python's <c>dispatch/provider_table.py</c>.
/// Provides provider lookup by ID, profile root, and tool queries.
/// </para>
/// </summary>
public sealed class ProviderTable
{
    private readonly ConcurrentDictionary<string, Provider> _providers = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _profileTools = new();
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// Add a provider to the table.
    /// </summary>
    public void Add(Provider provider)
    {
        _providers[provider.ProviderId] = provider;

        // Track tools by profile for quick lookups
        foreach (var toolId in provider.ToolIds)
        {
            var key = $"{provider.ProfileRoot}:{toolId}";
            _profileTools.GetOrAdd(key, _ => new HashSet<string>()).Add(provider.ProviderId);
        }
    }

    /// <summary>
    /// Get a provider by ID.
    /// </summary>
    public bool TryGetProvider(string providerId, out Provider? provider)
    {
        return _providers.TryGetValue(providerId, out provider);
    }

    /// <summary>
    /// Remove a provider from the table.
    /// </summary>
    public bool Remove(string providerId)
    {
        if (_providers.TryRemove(providerId, out var provider))
        {
            // Clean up profile tools
            foreach (var toolId in provider.ToolIds)
            {
                var key = $"{provider.ProfileRoot}:{toolId}";
                if (_profileTools.TryGetValue(key, out var providers))
                {
                    providers.Remove(providerId);
                    if (providers.Count == 0)
                    {
                        _profileTools.TryRemove(key, out _);
                    }
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Find providers that have a specific tool.
    /// </summary>
    /// <param name="toolId">Tool ID to search for.</param>
    /// <param name="profileRoot">Optional profile root to restrict search.</param>
    public IReadOnlyList<Provider> FindProviders(string toolId, string? profileRoot = null)
    {
        var result = new List<Provider>();

        foreach (var provider in _providers.Values)
        {
            if (provider.HasTool(toolId))
            {
                if (profileRoot == null || provider.ProfileRoot == profileRoot)
                {
                    result.Add(provider);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Find a provider for a tool using routing rules.
    /// <para>
    /// Priority:
    /// 1. Provider with matching profile root and tool
    /// 2. Provider with matching tool
    /// 3. First provider with matching tool
    /// </para>
    /// </summary>
    /// <param name="toolId">Tool ID to find.</param>
    /// <param name="profileRoot">Profile root to prefer.</param>
    /// <param name="routingKey">Routing key (same as profile root for client tools).</param>
    public Provider? ResolveProvider(string toolId, string? profileRoot = null, string? routingKey = null)
    {
        var candidates = FindProviders(toolId, profileRoot);

        // Prefer provider with matching profile root
        if (!string.IsNullOrEmpty(profileRoot))
        {
            foreach (var provider in candidates)
            {
                if (provider.ProfileRoot == profileRoot)
                {
                    return provider;
                }
            }
        }

        // Prefer provider with matching routing key
        if (!string.IsNullOrEmpty(routingKey))
        {
            foreach (var provider in candidates)
            {
                if (provider.ProfileRoot == routingKey)
                {
                    return provider;
                }
            }
        }

        // Return first available
        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Get all registered tools across all providers.
    /// </summary>
    public IReadOnlyList<ProviderTool> GetAllTools()
    {
        var result = new List<ProviderTool>();

        foreach (var provider in _providers.Values)
        {
            foreach (var toolId in provider.ToolIds)
            {
                result.Add(new ProviderTool
                {
                    ProviderId = provider.ProviderId,
                    ProviderName = provider.Metadata.Name,
                    ProviderType = provider.Metadata.Type,
                    ToolId = toolId,
                    ProfileRoot = provider.ProfileRoot
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Get the count of connected providers.
    /// </summary>
    public int ProviderCount => _providers.Count;

    /// <summary>
    /// Get all provider IDs.
    /// </summary>
    public IReadOnlyList<string> ProviderIds => _providers.Keys.ToList();
}

/// <summary>
/// Tool information from a provider.
/// </summary>
public sealed record ProviderTool
{
    /// <summary>
    /// Provider ID.
    /// </summary>
    public required string ProviderId { get; set; }

    /// <summary>
    /// Provider name.
    /// </summary>
    public required string ProviderName { get; set; }

    /// <summary>
    /// Provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// Fully-qualified tool ID.
    /// </summary>
    public required string ToolId { get; set; }

    /// <summary>
    /// Profile root of the provider.
    /// </summary>
    public required string ProfileRoot { get; set; }
}
