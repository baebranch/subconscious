namespace Subconscious.Engine.Tray;

/// <summary>
/// A single entry in the tray icon's context menu.
/// </summary>
/// <param name="Label">Text shown in the menu.</param>
/// <param name="OnClick">Invoked when the item is clicked.</param>
/// <param name="IsDefault">
/// Whether this is the menu's default action (invoked on a plain click/double-click
/// of the tray icon itself, not just via the context menu). Mirrors <c>pystray</c>'s
/// <c>MenuItem(..., default=True)</c> used for "Open Subconscious" in the Python app.
/// </param>
public sealed record TrayMenuItem(string Label, Action OnClick, bool IsDefault = false);

/// <summary>
/// Cross-platform abstraction over a system tray / notification-area icon.
///
/// Mirrors the role of <c>desktop/tray.py</c>'s <c>Tray</c> class: keeps the engine
/// resident in the background with a menu offering at least "open the UI" and "exit
/// entirely" actions. Platform-specific implementations are selected at runtime by
/// <see cref="Subconscious.Desktop.Tray.TrayIconServiceFactory"/> — see that project's
/// README for the current Windows-only support and the Phase 6 plan for native
/// macOS/Linux backends (most likely via Avalonia's own <c>TrayIcon</c> control once
/// the desktop UI framework decision is made).
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>
    /// Whether this platform has a real tray icon implementation. When false,
    /// <see cref="Show"/> is a safe no-op (see <c>NullTrayIconService</c>) so callers
    /// never need to branch on platform support themselves.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Create and display the tray icon with the given tooltip, icon file, and menu.
    /// Safe to call once per process lifetime; calling it again replaces the menu.
    /// </summary>
    void Show(string tooltip, string iconPath, IReadOnlyList<TrayMenuItem> menuItems);

    /// <summary>Remove the tray icon without disposing the underlying service.</summary>
    void Hide();
}
