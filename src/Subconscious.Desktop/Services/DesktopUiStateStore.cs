using System.Globalization;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.Services;

/// <summary>Desktop UI state read from and written to the engine's generic app-state API.</summary>
public sealed record DesktopUiState
{
    public string CurrentView { get; init; } = "Idle";
    public string CurrentContext { get; init; } = "Threads";
    public bool ContextVisible { get; init; } = true;
    public double ChatPanelWidth { get; init; } = 380;
    public double ContextPanelWidth { get; init; } = 360;
    public string? ActiveWorkspaceUuid { get; init; }
    public bool ShowAllThreads { get; init; }
    public string? SelectedThreadUuid { get; init; }
    public string? SelectedWorkspaceUuid { get; init; }
    public string? SelectedSetting { get; init; }
    public string ChatboxText { get; init; } = string.Empty;
}

/// <summary>Keeps Desktop state behind the engine API; the UI never opens SQLite directly.</summary>
public sealed class DesktopUiStateStore
{
    private const string Tag = "ui_state";
    private const string Client = "desktop";

    public async Task<DesktopUiState> LoadAsync(bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        var values = (await client.GetSettingsAsync(tag: Tag, client: Client, cancellationToken: cancellationToken))
            .ToDictionary(setting => setting.Key, setting => setting.Value);

        return new DesktopUiState
        {
            CurrentView = Read(values, "ui_current_view", "Idle"),
            CurrentContext = Read(values, "ui_current_context", "Threads"),
            ContextVisible = bool.TryParse(Read(values, "ui_context_visible", "true"), out var visible) && visible,
            ChatPanelWidth = ReadDouble(values, "ui_chat_panel_width", 380),
            ContextPanelWidth = ReadDouble(values, "ui_context_panel_width", 360),
            ActiveWorkspaceUuid = NullIfEmpty(Read(values, "ui_active_workspace_uuid", string.Empty)),
            ShowAllThreads = bool.TryParse(Read(values, "ui_show_all_threads", "false"), out var showAll) && showAll,
            SelectedThreadUuid = NullIfEmpty(Read(values, "ui_selected_thread_uuid", string.Empty)),
            SelectedWorkspaceUuid = NullIfEmpty(Read(values, "ui_selected_workspace_uuid", string.Empty)),
            SelectedSetting = NullIfEmpty(Read(values, "ui_selected_setting", string.Empty)),
            ChatboxText = Read(values, "ui_chatbox_text", string.Empty),
        };
    }

    public async Task SaveAsync(DesktopUiState state, bool dev, CancellationToken cancellationToken = default)
    {
        await using var client = new EngineClient();
        await client.ConnectRestAsync(dev);
        await client.UpdateSettingsAsync(
        [
            Setting("ui_current_view", state.CurrentView),
            Setting("ui_current_context", state.CurrentContext),
            Setting("ui_context_visible", state.ContextVisible.ToString().ToLowerInvariant()),
            Setting("ui_chat_panel_width", state.ChatPanelWidth.ToString(CultureInfo.InvariantCulture)),
            Setting("ui_context_panel_width", state.ContextPanelWidth.ToString(CultureInfo.InvariantCulture)),
            Setting("ui_active_workspace_uuid", state.ActiveWorkspaceUuid),
            Setting("ui_show_all_threads", state.ShowAllThreads.ToString().ToLowerInvariant()),
            Setting("ui_selected_thread_uuid", state.SelectedThreadUuid),
            Setting("ui_selected_workspace_uuid", state.SelectedWorkspaceUuid),
            Setting("ui_selected_setting", state.SelectedSetting),
            Setting("ui_chatbox_text", state.ChatboxText),
        ], cancellationToken);
    }

    private static AppStateSetting Setting(string key, string? value) =>
        new() { Key = key, Value = value ?? string.Empty, Tag = Tag, Client = Client };

    private static string Read(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static double ReadDouble(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        double.TryParse(Read(values, key, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value > 0 ? value : fallback;
}
