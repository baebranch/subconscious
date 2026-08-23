namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// Registry of configured tools (scripts, MCP servers, REST/API endpoints).
/// Tool types: 'script' (Python/JS/TS), 'mcp' (MCP server), 'api' (REST endpoint).
/// Endpoint API-key headers are stored as encrypted, write-only JSON keyed by UUID.
/// Status values: 'active', 'disabled', 'error'
/// </summary>
public class ToolRegistry
{
    public int Id { get; set; }
    public required string Uuid { get; set; }
    public required string Name { get; set; }
    public string? Alias { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// Tool type: 'script', 'mcp', 'api'
    /// </summary>
    public required string ToolType { get; set; } = "script";
    
    // Script-specific
    public string? ScriptPath { get; set; }
    /// <summary>
    /// Script language: 'python', 'javascript', 'typescript'
    /// </summary>
    public string? ScriptLanguage { get; set; }
    
    // MCP / API endpoint
    public string? EndpointUrl { get; set; }
    
    // Auth
    /// <summary>
    /// Authentication type; only <c>api_key</c> is currently supported for endpoint tools.
    /// </summary>
    public string? AuthType { get; set; }
    /// <summary>
    /// Legacy environment-variable metadata retained for compatibility; new header configurations
    /// are encrypted in the secret store instead.
    /// </summary>
    public string? AuthEnvVar { get; set; }
    
    /// <summary>
    /// Status: active, disabled, error
    /// </summary>
    public required string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
