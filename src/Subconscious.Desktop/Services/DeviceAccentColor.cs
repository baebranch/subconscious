namespace Subconscious.Desktop.Services;

/// <summary>
/// Reads the OS-level accent colour for the "Device" entry in the Colour Theme picker. MAUI has
/// no cross-platform API for this (same situation as <c>SplitterCursor</c>/<c>PointerCapture</c>),
/// so it's WinUI's <c>Windows.UI.ViewManagement.UISettings.GetColorValue</c> behind an
/// <c>#if WINDOWS</c>, with a fixed fallback everywhere else.
/// </summary>
internal static class DeviceAccentColor
{
    /// <summary>The OS accent colour, or null where it can't be read (non-Windows heads, or the
    /// WinUI API throwing) — callers fall back to
    /// <see cref="Resources.Styles.ThemePalette.DeviceFallbackAccent"/> in that case.</summary>
    public static Color? Get()
    {
#if WINDOWS
        try
        {
            var settings = new Windows.UI.ViewManagement.UISettings();
            var value = settings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromRgba(value.R, value.G, value.B, value.A);
        }
        catch (Exception)
        {
            return null;
        }
#else
        return null;
#endif
    }
}
