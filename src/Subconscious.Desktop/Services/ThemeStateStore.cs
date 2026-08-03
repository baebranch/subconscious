using System.Text.Json;
using Subconscious.Desktop.Engine;
using Subconscious.Desktop.Resources.Styles;

namespace Subconscious.Desktop.Services;

/// <summary>
/// The persisted half of theming: which <see cref="ThemeMode"/> and <see cref="AccentTheme"/> the
/// user picked. <see cref="ThemeService"/> is the runtime half — it owns applying whatever this
/// class loads/saves to <c>Application.Current.Resources</c>.
/// </summary>
public sealed class ThemeState
{
    /// <summary><see cref="ThemeMode.Device"/> is the default per the settings brief: a fresh
    /// install follows the OS light/dark setting until the user picks something else.</summary>
    public ThemeMode Mode { get; set; } = ThemeMode.Device;

    /// <summary><see cref="AccentTheme.Purple"/> is the app's original colour and what
    /// colours.txt's Python app used as its default seed — kept as the accent default so a fresh
    /// install still matches the design mock, while Mode defaults to following the OS.</summary>
    public AccentTheme Accent { get; set; } = AccentTheme.Purple;
}

/// <summary>
/// Reads/writes <see cref="ThemeState"/> as <c>desktop-theme.json</c> in the engine's data
/// directory — same directory, same file-over-<c>Preferences</c> reasoning as
/// <see cref="LayoutStateStore"/> (this app is unpackaged, so there's no MSIX identity backing
/// <c>Preferences</c> anyway). A separate file from <c>desktop-ui.json</c> because theme is
/// app-wide state and layout is per-window state; nothing requires them to load or fail together.
///
/// This is a stand-in for the engine's <c>app_state</c> table (see
/// <c>tests/Subconscious.Engine.Tests/app_state.txt</c>'s <c>mode</c>/<c>colour</c> rows, tag
/// <c>system</c>) — there is no HTTP endpoint for <c>app_state</c> yet. When one exists, only this
/// class's Load/Save need to change to call it instead of touching a local file; every caller
/// only ever sees <see cref="ThemeState"/>.
/// </summary>
public sealed class ThemeStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public ThemeStateStore(bool dev)
    {
        _filePath = Path.Combine(EngineDiscovery.DataDirectory(dev), "desktop-theme.json");
    }

    /// <summary>Loads persisted theme, falling back to defaults for a first run, a hand-edited
    /// file, or an unrecognized enum value — theme is never important enough to fail startup
    /// over, same policy as <see cref="LayoutStateStore.Load"/>.</summary>
    public ThemeState Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new ThemeState();
            }
            return JsonSerializer.Deserialize<ThemeState>(File.ReadAllText(_filePath)) ?? new ThemeState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ThemeState();
        }
    }

    public void Save(ThemeState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a failed theme write shouldn't interrupt what the user was doing.
        }
    }
}
