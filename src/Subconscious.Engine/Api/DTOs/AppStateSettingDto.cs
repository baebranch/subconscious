namespace Subconscious.Engine.Api.DTOs;

/// <summary>A client- and tag-scoped value in the engine's generic <c>app_state</c> store.</summary>
public sealed record AppStateSettingDto
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public string? Tag { get; init; }
    public string? Client { get; init; }
}
