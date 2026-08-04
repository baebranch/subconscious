namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// Simple contact book scoped to a workspace.
/// </summary>
public class Contact
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Workspace? Workspace { get; set; }
}
