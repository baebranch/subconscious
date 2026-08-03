namespace Subconscious.Desktop.Services;

/// <summary>
/// Forces the *native* window theme on Windows to match the app's chosen light/dark mode.
///
/// Why this exists in addition to <c>Application.UserAppTheme</c>: controls left with no explicit
/// style — the Settings form's Mode/Colour Theme <c>Picker</c>s and its Data Folder
/// <c>Entry</c>, deliberately unstyled per the "OS field styling" requirement — render through
/// WinUI's own Fluent theme resources, which key off the native <c>FrameworkElement</c> tree's
/// actual <c>RequestedTheme</c>. That cascades down from the native window's root content, not
/// from MAUI's <c>Application.UserAppTheme</c>: changing <c>UserAppTheme</c> after the window
/// already exists doesn't reliably force it (a known MAUI/WinUI limitation — there is no
/// supported cross-platform API for this, same situation as <c>SplitterCursor</c> reaching WinUI
/// directly for cursor shapes). Left unfixed, picking "Light" while the real Windows theme is
/// Dark renders those controls' text in Dark-theme white on top of this app's own Light-themed
/// (white) background — invisible text, which is exactly the bug this class exists to prevent.
///
/// Same approach as <c>SplitterCursor</c>/<c>PointerCapture</c>: reach the native platform view
/// through the MAUI <see cref="Window"/>'s <c>Handler</c>, Windows-only, best-effort.
/// </summary>
internal static class PlatformTheme
{
    /// <summary>Applies the selected light/dark mode to the native WinUI tree. Native controls
    /// retain the Windows accent: WinUI exposes no supported per-application replacement for the
    /// Picker popup's system selection marker.</summary>
    public static void Apply(bool dark)
    {
#if WINDOWS
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach (var window in app.Windows)
        {
            try
            {
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUiWindow
                    && winUiWindow.Content is Microsoft.UI.Xaml.FrameworkElement root)
                {
                    root.RequestedTheme = dark
                        ? Microsoft.UI.Xaml.ElementTheme.Dark
                        : Microsoft.UI.Xaml.ElementTheme.Light;

                    // The title-bar caption buttons are native AppWindow chrome rather than
                    // part of the XAML visual tree, so RequestedTheme above cannot style them.
                    // Set their theme explicitly to keep glyphs legible as the app switches
                    // independently of the Windows device theme.
                    if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                    {
                        var titleBar = winUiWindow.AppWindow.TitleBar;
                        titleBar.PreferredTheme = dark
                            ? Microsoft.UI.Windowing.TitleBarTheme.Dark
                            : Microsoft.UI.Windowing.TitleBarTheme.Light;

                        // PreferredTheme controls the caption-button state resources, but a custom
                        // MAUI title bar can retain the old default glyph colour after a live switch.
                        // Pin its idle/inactive glyphs to the selected app theme; hover/pressed
                        // colours remain WinUI-managed and already track the selected theme.
                        titleBar.ButtonForegroundColor = dark
                            ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                            : Windows.UI.Color.FromArgb(255, 0, 0, 0);
                        titleBar.ButtonInactiveForegroundColor = dark
                            ? Windows.UI.Color.FromArgb(255, 128, 128, 128)
                            : Windows.UI.Color.FromArgb(255, 96, 96, 96);
                    }
                }
            }
            catch (Exception)
            {
                // Best effort — MAUI's own UserAppTheme assignment in ThemeService.Apply still
                // takes care of every styled (DynamicResource-driven) control regardless.
            }
        }
#endif
    }
}
