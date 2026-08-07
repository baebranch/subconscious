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
    /// <summary>Applies the selected light/dark mode to the native WinUI tree and makes its
    /// caption bar use the same semantic surface and text colours as the MAUI content.</summary>
    public static void Apply(bool dark)
    {
#if WINDOWS
        if (Application.Current is not { } app)
        {
            return;
        }

        // ThemeService writes these resources before calling us. The fallbacks keep first-window
        // creation safe should this method run before the palette has been initialized.
        var surface = GetColor(app, "SurfaceColor", dark ? "#2C2C2C" : "#FFFFFF");
        var primaryText = GetColor(app, "PrimaryTextColor", dark ? "#F5F5F5" : "#1F1B2E");
        var secondaryText = GetColor(app, "SecondaryTextColor", dark ? "#C4C4C4" : "#8A8698");
        var hover = GetColor(app, "HoverColor", dark ? "#383838" : "#EFEEF4");

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

                    // Keep MAUI's title surface inside the native caption region. Restoring a
                    // separate standard caption would leave MAUI's AppTitle host below it and
                    // produce two stacked bars.
                    if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                    {
                        var titleBar = winUiWindow.AppWindow.TitleBar;
                        titleBar.ExtendsContentIntoTitleBar = true;
                        winUiWindow.ExtendsContentIntoTitleBar = true;
                        titleBar.PreferredTheme = dark
                            ? Microsoft.UI.Windowing.TitleBarTheme.Dark
                            : Microsoft.UI.Windowing.TitleBarTheme.Light;

                        var nativeSurface = ToWindowsColor(surface);
                        var nativePrimaryText = ToWindowsColor(primaryText);
                        var nativeSecondaryText = ToWindowsColor(secondaryText);
                        var nativeHover = ToWindowsColor(hover);
                        var nativeSurfaceBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(nativeSurface);

                        // MAUI's Windows title host reads this brush while it occupies the
                        // extended caption region. Override both the backing resource and alias
                        // so an already-created host repaints immediately with the app surface.
                        Microsoft.UI.Xaml.Application.Current.Resources["ActualWinUITitleBarBrush"] = nativeSurfaceBrush;
                        Microsoft.UI.Xaml.Application.Current.Resources["WinUITitleBarBrush"] = nativeSurfaceBrush;
                        root.Resources["ActualWinUITitleBarBrush"] = nativeSurfaceBrush;
                        root.Resources["WinUITitleBarBrush"] = nativeSurfaceBrush;

                        // Caption buttons remain OS-owned and keep native hit testing, dragging,
                        // snap layouts, and hover/pressed behavior.
                        titleBar.BackgroundColor = nativeSurface;
                        titleBar.InactiveBackgroundColor = nativeSurface;
                        titleBar.ForegroundColor = nativePrimaryText;
                        titleBar.InactiveForegroundColor = nativeSecondaryText;
                        titleBar.ButtonBackgroundColor = nativeSurface;
                        titleBar.ButtonInactiveBackgroundColor = nativeSurface;
                        titleBar.ButtonForegroundColor = nativePrimaryText;
                        titleBar.ButtonInactiveForegroundColor = nativeSecondaryText;
                        titleBar.ButtonHoverBackgroundColor = nativeHover;
                        titleBar.ButtonHoverForegroundColor = nativePrimaryText;
                        titleBar.ButtonPressedBackgroundColor = nativeHover;
                        titleBar.ButtonPressedForegroundColor = nativePrimaryText;
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

#if WINDOWS
    private static Color GetColor(Application app, string resourceKey, string fallback) =>
        app.Resources.TryGetValue(resourceKey, out var resource) && resource is Color color
            ? color
            : Color.FromArgb(fallback);

    private static Windows.UI.Color ToWindowsColor(Color color) => Windows.UI.Color.FromArgb(
        ToByte(color.Alpha),
        ToByte(color.Red),
        ToByte(color.Green),
        ToByte(color.Blue));

    private static byte ToByte(float value) => (byte)System.Math.Round(value * byte.MaxValue);
#endif
}
