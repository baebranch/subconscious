using Subconscious.Engine.Tray;

namespace Subconscious.Desktop.Tray;

/// <summary>
/// Resolves the best available <see cref="ITrayIconService"/> for the current OS.
/// Windows gets the real <see cref="WindowsTrayIconService"/>; every other platform
/// (and the plain <c>net10.0</c> TFM, e.g. when running headless in CI) gets
/// <see cref="NullTrayIconService"/> until Phase 6 adds native macOS/Linux backends.
/// </summary>
public static class TrayIconServiceFactory
{
#if WINDOWS
    /// <summary>
    /// Creates the tray icon and runs its Win32 message loop on a dedicated background
    /// STA thread, then hands back the (already-shown) service. <paramref name="onReady"/>
    /// runs after <see cref="ITrayIconService.Show"/> so callers can, e.g., surface a
    /// "tray icon ready" log line only once it is actually visible.
    ///
    /// The returned service's <see cref="ITrayIconService.Dispose"/> stops the message
    /// loop and joins the thread, so shutdown is deterministic.
    /// </summary>
    public static ITrayIconService CreateAndRun(
        string tooltip,
        string iconPath,
        IReadOnlyList<TrayMenuItem> menuItems,
        Action? onReady = null)
    {
        var service = new WindowsTrayIconService();
        var ready = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            service.Show(tooltip, iconPath, menuItems);
            ready.Set();
            onReady?.Invoke();
            service.Run(); // blocks until Hide()/Dispose() calls ExitThread()
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        return service;
    }
#endif

    /// <summary>
    /// True when this process/platform can actually show a tray icon. Callers (e.g. the
    /// CLI host) should check this before attempting <see cref="Create"/> so a
    /// <c>--headless</c> run or an unsupported platform never tries to spin up a
    /// background STA thread for nothing.
    /// </summary>
    public static bool IsSupported =>
#if WINDOWS
        true;
#else
        false;
#endif

    /// <summary>
    /// Creates the platform-appropriate tray icon service and immediately shows it.
    /// Returns a <see cref="NullTrayIconService"/> (a safe no-op) on unsupported platforms.
    /// </summary>
    public static ITrayIconService Create(
        string tooltip,
        string iconPath,
        IReadOnlyList<TrayMenuItem> menuItems,
        Action? onReady = null)
    {
#if WINDOWS
        return CreateAndRun(tooltip, iconPath, menuItems, onReady);
#else
        return new NullTrayIconService();
#endif
    }
}
