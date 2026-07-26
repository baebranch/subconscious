namespace Subconscious.Engine.Tray;

/// <summary>
/// No-op <see cref="ITrayIconService"/> for platforms/target frameworks without a tray
/// icon backend yet. Keeps callers (e.g. the CLI host) simple: they can always resolve
/// and call an <see cref="ITrayIconService"/> without checking <see cref="IsSupported"/>
/// first, and headless (<c>--headless</c>) runs never need a tray icon at all.
/// </summary>
public sealed class NullTrayIconService : ITrayIconService
{
    public bool IsSupported => false;

    public void Show(string tooltip, string iconPath, IReadOnlyList<TrayMenuItem> menuItems)
    {
        // Intentionally does nothing — no tray backend is available.
    }

    public void Hide()
    {
    }

    public void Dispose()
    {
    }
}
