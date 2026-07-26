using Microsoft.Extensions.Hosting;
using Subconscious.Engine;
using Subconscious.Engine.Tray;
using Subconscious.Desktop.Tray;

namespace Subconscious.Host.Cli;

/// <summary>
/// Wires an <see cref="ITrayIconService"/> to a running engine <see cref="IHost"/>:
/// "Exit" stops the host (ending the process), and "Open Subconscious" is the tray's
/// default action (double-click / first menu item), mirroring <c>desktop/tray.py</c>'s
/// <c>__default_tray_option</c> / <c>__tray_exit</c> handlers.
///
/// "Open Subconscious" currently just logs, since the desktop UI client doesn't exist
/// yet (Phase 6) — there's nothing to open or bring to the foreground. Once it does,
/// this is the seam where it gets wired up: launch the desktop process if not running,
/// or signal an already-running one to show its window.
///
/// The tray icon is attached regardless of <see cref="EngineConfig.Headless"/>:
/// <c>--headless</c> only skips the desktop GUI window (once one exists, Phase 6), not
/// the tray icon, which is how the engine stays reachable ("open Subconscious", "exit")
/// even in a headless run. It is only ever withheld when the current platform has no
/// tray backend at all (<see cref="TrayIconServiceFactory.IsSupported"/> is false).
/// </summary>
public static class EngineTrayCoordinator
{
    // Matches the Assets\favicon.ico item in Subconscious.Host.Cli.csproj, which copies
    // with CopyToOutputDirectory="PreserveNewest" - preserving the "Assets" subfolder
    // under the build output rather than flattening it to the output root.
    private const string IconFileName = "Assets/favicon.ico";

    /// <summary>
    /// Attaches a tray icon to <paramref name="host"/> when the current platform supports
    /// one — independent of <see cref="EngineConfig.Headless"/> (see class remarks).
    /// Returns null (nothing to dispose) only when no tray backend exists yet for this
    /// platform.
    /// </summary>
    public static ITrayIconService? AttachIfSupported(IHost host, EngineConfig config)
    {
        if (!TrayIconServiceFactory.IsSupported)
        {
            return null;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, IconFileName);
        var menuItems = new List<TrayMenuItem>
        {
            new(
                "Open Subconscious",
                () => Console.WriteLine(
                    "[tray] Open Subconscious requested — desktop client not implemented yet (Phase 6)."),
                IsDefault: true),
            new("Exit", () => _ = host.StopAsync()),
        };

        return TrayIconServiceFactory.Create(
            tooltip: $"Subconscious {Constants.Version}",
            iconPath: iconPath,
            menuItems: menuItems);
    }
}
