using Subconscious.Desktop.Engine;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Services;

/// <summary>Reads and writes the validated desktop panel configuration through the engine's
/// <c>app_state</c> settings endpoint. The desktop never reads the engine database directly.</summary>
public sealed class PanelConfigurationStore
{
    public async Task<PanelConfiguration> LoadAsync(bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        var setting = await client.GetPanelConfigurationAsync(cancellationToken);
        return Enum.TryParse<PanelConfiguration>(setting.Configuration, out var configuration)
            ? configuration
            : PanelConfiguration.ContextChatMain;
    }

    public async Task SaveAsync(PanelConfiguration configuration, bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        await client.UpdatePanelConfigurationAsync(configuration.ToString(), cancellationToken);
    }
}
