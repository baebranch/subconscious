using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Configuration;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Subconscious.Engine.Tools;

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
        var toolAuthConfigIds = await _credentials.GetToolAuthConfigIdsAsync(cancellationToken);
        return tools.Select(tool => MapToDto(tool, toolAuthConfigIds.Contains(tool.Uuid))).ToList();
    }

    public async Task<ToolRegistryDto?> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var tool = await _context.ToolRegistry.AsNoTracking().SingleOrDefaultAsync(tool => tool.Uuid == uuid, cancellationToken);
        if (tool is null)
        {
            return null;
        }
        var toolAuthConfigIds = await _credentials.GetToolAuthConfigIdsAsync(cancellationToken);
        return MapToDto(tool, toolAuthConfigIds.Contains(tool.Uuid));
    }

    public async Task<ToolRegistryDto> CreateAsync(UpsertToolRegistryRequest request, CancellationToken cancellationToken = default)
    {
        var toolType = RequiredOrDefault(request.ToolType, "script", "Tool type");
        ValidateAuthConfig(toolType, request.AuthType, request.AuthConfigJson);
        var uuid = Guid.NewGuid().ToString();
        var alias = ResolveAlias(request, null);
        var tool = new ToolRegistry
        {
            Uuid = uuid,
            Name = Normalize(request.Name) ?? SafeName(alias, uuid),
            Alias = alias,
            Description = request.Description,
            ToolType = toolType,
            ScriptPath = request.ScriptPath,
            ScriptLanguage = request.ScriptLanguage,
            EndpointUrl = request.EndpointUrl,
            AuthType = NormalizeAuthType(request.AuthType),
            AuthEnvVar = null,
            Status = RequiredOrDefault(request.Status, "active", "Status"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.ToolRegistry.Add(tool);
        await _context.SaveChangesAsync(cancellationToken);
        var hasAuthConfig = await UpdateAuthConfigAsync(tool, request, cancellationToken);
        return MapToDto(tool, hasAuthConfig);
    }

    public async Task<ToolRegistryDto?> UpdateAsync(string uuid, UpsertToolRegistryRequest request, CancellationToken cancellationToken = default)
    {
        var tool = await _context.ToolRegistry.SingleOrDefaultAsync(tool => tool.Uuid == uuid, cancellationToken);
        if (tool is null)
        {
            return null;
        }

        var toolType = RequiredOrDefault(request.ToolType, tool.ToolType, "Tool type");
        ValidateAuthConfig(toolType, request.AuthType, request.AuthConfigJson);
        tool.Name = Normalize(request.Name) ?? tool.Name;
        tool.Alias = ResolveAlias(request, tool);
        tool.Description = request.Description;
        tool.ToolType = toolType;
        tool.ScriptPath = request.ScriptPath;
        tool.ScriptLanguage = request.ScriptLanguage;
        tool.EndpointUrl = request.EndpointUrl;
        tool.AuthType = NormalizeAuthType(request.AuthType);
        tool.AuthEnvVar = null;
        tool.Status = RequiredOrDefault(request.Status, tool.Status, "Status");
        tool.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        var hasAuthConfig = await UpdateAuthConfigAsync(tool, request, cancellationToken);
        return MapToDto(tool, hasAuthConfig);
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
        await _credentials.RemoveToolAuthConfigAsync(uuid, cancellationToken);
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

    private async Task<bool> UpdateAuthConfigAsync(
        ToolRegistry tool,
        UpsertToolRegistryRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(tool.AuthType, "api_key", StringComparison.Ordinal))
        {
            await _credentials.RemoveToolAuthConfigAsync(tool.Uuid, cancellationToken);
            return false;
        }

        return await _credentials.UpdateToolAuthConfigAsync(
            tool.Uuid,
            Normalize(request.AuthConfigJson),
            request.ClearAuthConfig,
            cancellationToken);
    }

    private static ToolRegistryDto MapToDto(ToolRegistry tool, bool hasAuthConfig) => new()
    {
        Id = tool.Id, Uuid = tool.Uuid, Name = tool.Name, Alias = tool.Alias, Description = tool.Description,
        ToolType = tool.ToolType, ScriptPath = tool.ScriptPath, ScriptLanguage = tool.ScriptLanguage,
        EndpointUrl = tool.EndpointUrl,
        AuthType = tool.AuthType ?? (hasAuthConfig ? "api_key" : null),
        HasAuthConfig = hasAuthConfig,
        Status = tool.Status, CreatedAt = tool.CreatedAt, UpdatedAt = tool.UpdatedAt,
    };

    private static void ValidateAuthConfig(string toolType, string? authType, string? authConfigJson)
    {
        var normalizedAuthType = NormalizeAuthType(authType);
        if (string.Equals(normalizedAuthType, "api_key", StringComparison.Ordinal)
            && !IsEndpointToolType(toolType))
        {
            throw new ArgumentException("API key authentication is supported only for API and MCP endpoint tools.");
        }

        var normalizedConfig = Normalize(authConfigJson);
        if (normalizedConfig is null)
        {
            return;
        }
        if (!string.Equals(normalizedAuthType, "api_key", StringComparison.Ordinal))
        {
            throw new ArgumentException("API key headers require the api_key authentication type.");
        }

        try
        {
            using var document = JsonDocument.Parse(normalizedConfig);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("API key headers must be a non-empty JSON object with valid, unique HTTP header names and non-empty string values.");
            }

            var headers = document.RootElement.EnumerateObject().ToList();
            if (headers.Count == 0
                || headers.Select(header => header.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count
                || !headers.All(header =>
                    IsValidHeaderName(header.Name)
                    && header.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(header.Value.GetString())
                    && !ContainsLineBreak(header.Value.GetString()!)))
            {
                throw new ArgumentException("API key headers must be a non-empty JSON object with valid, unique HTTP header names and non-empty string values.");
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("API key headers must be valid JSON.", exception);
        }
    }

    private static bool IsEndpointToolType(string toolType) =>
        string.Equals(toolType, "api", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolType, "mcp", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidHeaderName(string value) =>
        value.Length > 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));

    private static bool ContainsLineBreak(string value) => value.Contains('\r') || value.Contains('\n');

    private static string? NormalizeAuthType(string? value)
    {
        var normalized = Normalize(value)?.ToLowerInvariant();
        return normalized switch
        {
            null or "api_key" => normalized,
            _ => throw new ArgumentException("Only api_key authentication is supported for configured tools."),
        };
    }

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
