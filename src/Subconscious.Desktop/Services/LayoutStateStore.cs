using System.Text.Json;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.Services;

/// <summary>
/// The bits of window layout that survive a restart: how wide the user dragged each panel, and
/// whether the right-hand context panel is showing.
/// </summary>
public sealed class LayoutState
{
    public double ChatPanelWidth { get; set; } = 380;
    public double ContextPanelWidth { get; set; } = 360;
    public bool IsContextPanelOpen { get; set; } = true;

    /// <summary>Name of a <c>ContextPanelSection</c> value — stored as a string so an unknown
    /// value from a newer build degrades to the default instead of throwing.</summary>
    public string ContextSection { get; set; } = "Threads";
}

/// <summary>
/// Reads/writes <see cref="LayoutState"/> as <c>desktop-ui.json</c> in the engine's data
/// directory (the same folder <c>runtime.json</c> lives in, so a <c>--dev</c> run keeps its own
/// layout).
///
/// Why a JSON file rather than MAUI's <c>Preferences</c>: this app is unpackaged
/// (<c>WindowsPackageType=None</c>), so there's no MSIX identity backing per-app settings
/// storage, and a plain file behaves the same on every head. If client UI state later moves into
/// the engine's <c>app_state</c> table (it already has a (key, tag) store), only this class
/// changes — callers just see Load/Save.
/// </summary>
public sealed class LayoutStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public LayoutStateStore(bool dev)
    {
        _filePath = Path.Combine(EngineDiscovery.DataDirectory(dev), "desktop-ui.json");
    }

    /// <summary>Loads persisted layout, falling back to defaults for a first run, a hand-edited
    /// file, or anything else unreadable — layout is never important enough to fail startup over.</summary>
    public LayoutState Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new LayoutState();
            }
            return JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(_filePath)) ?? new LayoutState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new LayoutState();
        }
    }

    public void Save(LayoutState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a failed layout write shouldn't interrupt what the user was doing.
        }
    }
}
