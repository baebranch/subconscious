namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// A Subconscious Network - top-level organizational entity.
/// </summary>
public class Network
{
    public int Id { get; set; }
    public required string Uuid { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? DefaultWorkspaceUuid { get; set; }
    public byte[]? Passphrase { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ICollection<Workspace> Workspaces { get; set; } = new List<Workspace>();
}
