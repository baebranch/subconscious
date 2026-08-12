using System.Globalization;
using System.Text.Json;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.Services;

/// <summary>Normal (non-maximized) native window bounds, expressed in WinUI physical pixels.</summary>
public sealed record DesktopWindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);

/// <summary>Desktop UI state read from and written to the engine's generic app-state API.</summary>
public sealed record DesktopUiState
{
    public string CurrentView { get; init; } = "threads";
    public string CurrentContext { get; init; } = "threads";
    public bool ContextVisible { get; init; } = true;
    public double ChatPanelWidth { get; init; } = 380;
    public double ContextPanelWidth { get; init; } = 360;
    public DesktopWindowPlacement? WindowPlacement { get; init; }
    public int? ActiveWorkspaceId { get; init; }
    public bool ShowAllThreads { get; init; }
    public int? SelectedThreadId { get; init; }
    public int? SelectedWorkspaceId { get; init; }
    public string? SelectedSetting { get; init; }
    public string ChatboxText { get; init; } = string.Empty;
    public FileWorkspaceNavigationState FileNavigation { get; init; } = FileWorkspaceNavigationState.Empty;
}

/// <summary>A compact, durable file-tree and editor-tab navigation snapshot.</summary>
public sealed record FileWorkspaceNavigationState
{
    public static FileWorkspaceNavigationState Empty { get; } = new();

    public string? TreeWorkspaceUuid { get; init; }
    public string? TreeDirectories { get; init; }
    public IReadOnlyList<FileTreeDirectoryReference> ExpandedDirectories { get; init; } = [];
    public IReadOnlyList<FileEditorDocumentReference> OpenTabs { get; init; } = [];
    public FileEditorDocumentReference? SelectedTab { get; init; }
}

/// <summary>Stable identity of an expanded directory within a workspace's allow-list.</summary>
public sealed record FileTreeDirectoryReference(int RootIndex, string RelativePath);

/// <summary>Stable identity of a real engine-backed editor document.</summary>
public sealed record FileEditorDocumentReference(string WorkspaceUuid, int RootIndex, string RelativePath);

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
        var currentView = Read(values, "ui_current_view", "threads");

        return new DesktopUiState
        {
            CurrentView = currentView,
            CurrentContext = Read(values, "ui_current_context", currentView),
            ContextVisible = bool.TryParse(Read(values, "ui_context_visible", "true"), out var visible) && visible,
            ChatPanelWidth = ReadDouble(values, "ui_chat_panel_width", 380),
            ContextPanelWidth = ReadDouble(values, "ui_context_panel_width", 360),
            WindowPlacement = ReadWindowPlacement(values),
            ActiveWorkspaceId = ReadId(values, "ui_active_workspace_id"),
            ShowAllThreads = bool.TryParse(Read(values, "ui_show_all_threads", "false"), out var showAll) && showAll,
            SelectedThreadId = ReadId(values, "ui_selected_thread_id"),
            SelectedWorkspaceId = ReadId(values, "ui_selected_workspace_id"),
            SelectedSetting = NullIfEmpty(Read(values, "ui_selected_setting", string.Empty)),
            ChatboxText = Read(values, "ui_chatbox_text", string.Empty),
            FileNavigation = ReadFileNavigationState(values),
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
            Setting("ui_window_x", FormatPlacementValue(state.WindowPlacement?.X)),
            Setting("ui_window_y", FormatPlacementValue(state.WindowPlacement?.Y)),
            Setting("ui_window_width", FormatPlacementValue(state.WindowPlacement?.Width)),
            Setting("ui_window_height", FormatPlacementValue(state.WindowPlacement?.Height)),
            Setting("ui_window_maximized", state.WindowPlacement?.IsMaximized.ToString().ToLowerInvariant()),
            Setting("ui_active_workspace_id", FormatId(state.ActiveWorkspaceId)),
            Setting("ui_show_all_threads", state.ShowAllThreads.ToString().ToLowerInvariant()),
            Setting("ui_selected_thread_id", FormatId(state.SelectedThreadId)),
            Setting("ui_selected_workspace_id", FormatId(state.SelectedWorkspaceId)),
            Setting("ui_selected_setting", state.SelectedSetting?.ToLowerInvariant()),
            Setting("ui_chatbox_text", state.ChatboxText),
            Setting("ui_file_navigation", SerializeFileNavigationState(state.FileNavigation)),
            Setting("ui_chatbox_attachments", "[]"),
            Setting("ui_unread_threads", "[]"),
        ], cancellationToken);
    }

    private static AppStateSetting Setting(string key, string? value) =>
        new() { Key = key, Value = value ?? string.Empty, Tag = Tag, Client = Client };

    private static string Read(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? ReadId(IReadOnlyDictionary<string, string> values, string key) =>
        int.TryParse(Read(values, key, string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;

    private static string? FormatId(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static DesktopWindowPlacement? ReadWindowPlacement(IReadOnlyDictionary<string, string> values)
    {
        if (!TryReadInt(values, "ui_window_x", out var x)
            || !TryReadInt(values, "ui_window_y", out var y)
            || !TryReadInt(values, "ui_window_width", out var width)
            || !TryReadInt(values, "ui_window_height", out var height)
            || width <= 0 || height <= 0)
        {
            return null;
        }

        var isMaximized = bool.TryParse(Read(values, "ui_window_maximized", "false"), out var maximized)
            && maximized;
        return new DesktopWindowPlacement(x, y, width, height, isMaximized);
    }

    private static bool TryReadInt(IReadOnlyDictionary<string, string> values, string key, out int value) =>
        int.TryParse(Read(values, key, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static string? FormatPlacementValue(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static FileWorkspaceNavigationState ReadFileNavigationState(IReadOnlyDictionary<string, string> values)
    {
        try
        {
            var state = JsonSerializer.Deserialize<FileWorkspaceNavigationState>(
                Read(values, "ui_file_navigation", string.Empty));
            if (state is null)
            {
                return FileWorkspaceNavigationState.Empty;
            }

            var tabs = (state.OpenTabs ?? [])
                .Where(reference => reference is not null && IsValidDocumentReference(reference))
                .Distinct()
                .ToArray();
            var expanded = (state.ExpandedDirectories ?? [])
                .Where(reference => reference is not null && IsValidDirectoryReference(reference))
                .Distinct()
                .ToArray();
            var selected = state.SelectedTab is { } candidate
                && IsValidDocumentReference(candidate)
                && tabs.Contains(candidate)
                    ? candidate
                    : null;
            var hasTree = !string.IsNullOrWhiteSpace(state.TreeWorkspaceUuid)
                && !string.IsNullOrWhiteSpace(state.TreeDirectories);

            return new FileWorkspaceNavigationState
            {
                TreeWorkspaceUuid = hasTree ? state.TreeWorkspaceUuid : null,
                TreeDirectories = hasTree ? state.TreeDirectories : null,
                ExpandedDirectories = hasTree ? expanded : [],
                OpenTabs = tabs,
                SelectedTab = selected,
            };
        }
        catch (JsonException)
        {
            return FileWorkspaceNavigationState.Empty;
        }
    }

    private static string SerializeFileNavigationState(FileWorkspaceNavigationState state) =>
        JsonSerializer.Serialize(state);

    private static bool IsValidDirectoryReference(FileTreeDirectoryReference reference) =>
        reference.RootIndex >= 0 && reference.RelativePath is not null;

    private static bool IsValidDocumentReference(FileEditorDocumentReference reference) =>
        !string.IsNullOrWhiteSpace(reference.WorkspaceUuid)
        && reference.RootIndex >= 0
        && !string.IsNullOrWhiteSpace(reference.RelativePath);

    private static double ReadDouble(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        double.TryParse(Read(values, key, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value > 0 ? value : fallback;
}
