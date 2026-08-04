namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// A message in a conversation thread.
/// Role values: user, assistant, system, tool
/// </summary>
public class Message
{
    public int Id { get; set; }
    public required string Uuid { get; set; }
    public int ThreadId { get; set; }
    /// <summary>
    /// Message role: user, assistant, system, tool
    /// </summary>
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Thread? Thread { get; set; }
}
