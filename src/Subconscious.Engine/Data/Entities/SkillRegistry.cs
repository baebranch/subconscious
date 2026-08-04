namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// Registry of installed skills (packages containing agent capabilities).
/// Skills are stored as packages in the config data folder under skills/{uuid}.
/// Source can be a URL (git/zip download), a local zip file, or a local folder.
/// Status values: 'pending', 'valid', 'invalid', 'error'
/// </summary>
public class SkillRegistry
{
    public int Id { get; set; }
    public required string Uuid { get; set; }
    public required string Name { get; set; }
    public string? Alias { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// URL, zip path, or folder path.
    /// </summary>
    public required string Source { get; set; }
    /// <summary>
    /// Source type: 'url', 'zip', 'folder'
    /// </summary>
    public required string SourceType { get; set; } = "folder";
    /// <summary>
    /// Resolved path inside data_dir/skills/
    /// </summary>
    public string? InstallPath { get; set; }
    public string? Version { get; set; }
    /// <summary>
    /// Status: pending, valid, invalid, error
    /// </summary>
    public required string Status { get; set; } = "pending";
    /// <summary>
    /// JSON list of tool slugs declared in skill.json
    /// </summary>
    public string? RequiredTools { get; set; }
    /// <summary>
    /// Raw skill.json / manifest contents
    /// </summary>
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
