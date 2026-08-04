namespace Subconscious.Engine.Data.Entities;

/// <summary>
/// Store application state using key-value pairs with optional tags and client scopes.
/// A (key, tag, client) tuple is unique.
/// </summary>
public class AppState
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    /// <summary>
    /// Optional tag to categorize state/settings (e.g., "system", "user", etc.)
    /// </summary>
    public string? Tag { get; set; }
    /// <summary>Optional client scope (for example, <c>desktop</c> or <c>browser</c>).</summary>
    public string? Client { get; set; }
}
