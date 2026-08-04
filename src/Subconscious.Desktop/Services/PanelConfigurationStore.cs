using Subconscious.Desktop.Engine;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Services;

/// <summary>Reads and writes the validated desktop panel configuration through the engine's
/// generic client-scoped <c>app_state</c> endpoint. The desktop never reads the engine database.</summary>
public sealed class PanelConfigurationStore
{
    private const string Key = "panel_configuration";
    private const string Tag = "ui_state";
    private const string Client = "desktop";

    public async Task<PanelConfiguration> LoadAsync(bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        var value = (await client.GetSettingsAsync(Key, Tag, Client, cancellationToken))
            .SingleOrDefault()?.Value;
        return Enum.TryParse<PanelConfiguration>(value, out var configuration)
            && Enum.IsDefined(configuration)
                ? configuration
                : PanelConfiguration.ContextChatMain;
    }

    public async Task SaveAsync(PanelConfiguration configuration, bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        await client.UpdateSettingsAsync(
        [
            new AppStateSetting { Key = Key, Value = configuration.ToString(), Tag = Tag, Client = Client },
        ], cancellationToken);
    }
}
