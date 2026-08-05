using Subconscious.Desktop.Engine;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Services;

/// <summary>Reads and writes the navigation rail's validated position through the engine's
/// generic client-scoped <c>app_state</c> endpoint.</summary>
public sealed class SidebarPositionStore
{
    private const string Key = "sidebar_position";
    private const string Tag = "ui_state";
    private const string Client = "desktop";

    public async Task<SidebarPosition> LoadAsync(bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        var value = (await client.GetSettingsAsync(Key, Tag, Client, cancellationToken))
            .SingleOrDefault()?.Value;
        return Enum.TryParse<SidebarPosition>(value, ignoreCase: true, out var position)
            && Enum.IsDefined(position)
                ? position
                : SidebarPosition.Left;
    }

    public async Task SaveAsync(SidebarPosition position, bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        await client.UpdateSettingsAsync(
        [
            new AppStateSetting
            {
                Key = Key,
                Value = position.ToString().ToLowerInvariant(),
                Tag = Tag,
                Client = Client,
            },
        ], cancellationToken);
    }
}
