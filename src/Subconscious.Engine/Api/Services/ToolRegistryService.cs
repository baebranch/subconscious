using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Configuration;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Subconscious.Engine.Tools;
using System.Text;

namespace Subconscious.Engine.Api.Services;

/// <summary>Persists the public metadata for configured tools and combines it with the real built-in catalog.</summary>
public sealed class ToolRegistryService : IToolRegistryService
{
    private readonly SubconsciousDbContext _context;
    private readonly BaseToolRegistry _baseToolRegistry;
    private readonly IModelConfigurationStore _credentials;

    public ToolRegistryService(
        SubconsciousDbContext context,
        BaseToolRegistry baseToolRegistry,
        IModelConfigurationStore credentials)
    {
        _context = context;
        _baseToolRegistry = baseToolRegistry;
        _credentials = credentials;
    }

    public async Task<List<ToolRegistryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _context.ToolRegistry.AsNoTracking().OrderBy(tool => tool.Alias).ThenBy(tool => tool.Name)
            .ToListAsync(cancellationToken);
        var toolApiKeyIds = await _credentials.GetToolApiKeyIdsAsync(cancellationToken);
        return tools.Select(tool => MapToDto(tool, toolApiKeyIds.Contains(tool.Uuid))).ToList();
    }

    public async Task<ToolRegistryDto?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var tool = await _context.ToolRegistry.AsNoTracking().SingleOrDefaultAsync(tool => tool.Uuid == uuid, cancellationToken);
        if (tool is null)
        {
            return null;
        }
        var toolApiKeyIds = await _credentials.GetToolApiKeyIdsAsync(cancellationToken);
        return MapToDto(tool, toolApiKeyIds.Contains(tool.Uuid));
    }

    public async Task<ToolRegistryDto> CreateAsync(UpsertToolRegistryRequest request, CancellationToken cancellationToken = default)
    {
        var uuid = Guid.NewGuid().ToString();
        var alias = ResolveAlias(request, null);
        var tool = new ToolRegistry
        {
            Uuid = uuid,
            Name = Normalize(request.Name) ?? SafeName(alias, uuid),
            Alias = alias,
            Description = request.Description,
            ToolType = RequiredOrDefault(request.ToolType, "script", "Tool type"),
            ScriptPath = request.ScriptPath,
            ScriptLanguage = request.ScriptLanguage,
            EndpointUrl = request.EndpointUrl,
            AuthType = NormalizeAuthType(request.AuthType),
            AuthEnvVar = request.AuthEnvVar,
            Status = RequiredOrDefault(request.Status, "active", "Status"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.ToolRegistry.Add(tool);
        await _context.SaveChangesAsync(cancellationToken);
        var hasApiKey = await UpdateApiKeyAsync(tool, request, cancellationToken);
        return MapToDto(tool, hasApiKey);
    }

    public async Task<ToolRegistryDto?> UpdateAsync(string uuid, UpsertToolRegistryRequest request, CancellationToken cancellationToken = default)
    {
        var tool = await _context.ToolRegistry.SingleOrDefaultAsync(tool => tool.Uuid == uuid, cancellationToken);
        if (tool is null)
        {
            return null;
        }

        tool.Name = Normalize(request.Name) ?? tool.Name;
        tool.Alias = ResolveAlias(request, tool);
        tool.Description = request.Description;
        tool.ToolType = RequiredOrDefault(request.ToolType, tool.ToolType, "Tool type");
        tool.ScriptPath = request.ScriptPath;
        tool.ScriptLanguage = request.ScriptLanguage;
        tool.EndpointUrl = request.EndpointUrl;
        tool.AuthType = NormalizeAuthType(request.AuthType);
        tool.AuthEnvVar = request.AuthEnvVar;
        tool.Status = RequiredOrDefault(request.Status, tool.Status, "Status");
        tool.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        var hasApiKey = await UpdateApiKeyAsync(tool, request, cancellationToken);
        return MapToDto(tool, hasApiKey);
    }

    public async Task<bool> DeleteAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var tool = await _context.ToolRegistry.SingleOrDefaultAsync(tool => tool.Uuid == uuid, cancellationToken);
        if (tool is null)
        {
            return false;
        }
        _context.ToolRegistry.Remove(tool);
        await _context.SaveChangesAsync(cancellationToken);
        await _credentials.RemoveToolApiKeyAsync(uuid, cancellationToken);
        return true;
    }

    public async Task<ToolCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var builtin = _baseToolRegistry.Catalog().ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<BuiltinToolCatalogEntryDto>)group.Value.Select(entry => new BuiltinToolCatalogEntryDto
            {
                Name = entry.Name,
                Doc = entry.Doc,
                // Desktop's public wire model deliberately uses a stable JSON string, never
                // the numeric serialization of the internal OperationKind enum.
                Operation = entry.Operation.ToString().ToLowerInvariant(),
            }).ToList(),
            StringComparer.Ordinal);
        return new ToolCatalogDto { Builtin = builtin, Configured = await GetAllAsync(cancellationToken) };
    }

    private async Task<bool> UpdateApiKeyAsync(
        ToolRegistry tool,
        UpsertToolRegistryRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(tool.AuthType, "api_key", StringComparison.Ordinal))
        {
            await _credentials.RemoveToolApiKeyAsync(tool.Uuid, cancellationToken);
            return false;
        }

        return await _credentials.UpdateToolApiKeyAsync(
            tool.Uuid,
            Normalize(request.ApiKey),
            request.ClearApiKey,
            cancellationToken);
    }

    private static ToolRegistryDto MapToDto(ToolRegistry tool, bool hasApiKey) => new()
    {
        Id = tool.Id, Uuid = tool.Uuid, Name = tool.Name, Alias = tool.Alias, Description = tool.Description,
        ToolType = tool.ToolType, ScriptPath = tool.ScriptPath, ScriptLanguage = tool.ScriptLanguage,
        EndpointUrl = tool.EndpointUrl, AuthType = tool.AuthType,
        HasApiKey = string.Equals(tool.AuthType, "api_key", StringComparison.Ordinal) && hasApiKey,
        AuthEnvVar = tool.AuthEnvVar,
        Status = tool.Status, CreatedAt = tool.CreatedAt, UpdatedAt = tool.UpdatedAt,
    };

    private static string? NormalizeAuthType(string? value) => Normalize(value)?.ToLowerInvariant();

    private static string ResolveAlias(UpsertToolRegistryRequest request, ToolRegistry? existing)
    {
        var alias = Normalize(request.Alias) ?? existing?.Alias ?? Normalize(request.Name) ?? existing?.Name;
        return alias ?? throw new ArgumentException("A tool alias or name is required.");
    }

    private static string RequiredOrDefault(string? value, string fallback, string label)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} cannot be blank.");
        }
        return Normalize(value) ?? fallback;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SafeName(string alias, string uuid)
    {
        var builder = new StringBuilder("tool-");
        var pendingSeparator = false;
        foreach (var character in alias.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 5) builder.Append('-');
                builder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }
        return builder.Length > 5 ? builder.ToString() : $"tool-{uuid}";
    }
}
