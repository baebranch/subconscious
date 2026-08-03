namespace Subconscious.Desktop.Resources.Styles;

/// <summary>Light/dark selection for the app. <see cref="Device"/> follows
/// <c>Application.Current.RequestedTheme</c> — the OS setting — rather than storing an explicit
/// choice, and is the default on first run (see <see cref="Services.ThemeState"/>).</summary>
public enum ThemeMode
{
    Device,
    Light,
    Dark,
}

/// <summary>Accent colour options, matching the seed list in <c>colours.txt</c> 1:1 (that file's
/// <c>ft.Theme(color_scheme_seed=ft.Colors.X)</c> entries — the accent palette the original Flet
/// app supported). <see cref="Device"/> is additional: there is no equivalent entry in
/// colours.txt because the Flet app had no "follow the OS accent colour" option, but Windows and
/// most desktop shells do have a user-chosen accent colour, so it's offered here the same way
/// <see cref="ThemeMode.Device"/> is offered for light/dark.</summary>
public enum AccentTheme
{
    Device,
    Purple,
    Blue,
    Teal,
    Green,
    Yellow,
    Orange,
    Red,
    Pink,
}

/// <summary>
/// Builds the full semantic colour set (see AppTheme.xaml's "Palette" region for the exact key
/// list) for a given effective light/dark mode and accent seed. Pure colour math — no MAUI
/// Application/Resources access here, so it's trivial to unit test independently of the UI stack.
/// <see cref="ThemeService"/> is what actually writes the result into
/// <c>Application.Current.Resources</c>.
/// </summary>
public static class ThemePalette
{
    /// <summary>
    /// Accent seed colours. Sourced from <c>colours.txt</c>'s Flet/Flutter Material colour names
    /// (<c>ft.Colors.DEEP_PURPLE</c> etc.), using each name's Material "500" swatch value, with one
    /// deliberate deviation: Material Yellow 500 (#FFEB3B) is too light for the white glyphs this
    /// app draws on top of the accent colour (send/stop icons, AccentButton, RoundAccentButton) to
    /// stay legible, so Yellow uses Amber/Yellow 800 (#F9A825) instead — still reads as "yellow"
    /// next to the other seven, but keeps enough contrast.
    /// </summary>
    private static readonly Dictionary<AccentTheme, Color> AccentSeeds = new()
    {
        [AccentTheme.Purple] = Color.FromArgb("#673AB7"),
        [AccentTheme.Blue] = Color.FromArgb("#2196F3"),
        [AccentTheme.Teal] = Color.FromArgb("#009688"),
        [AccentTheme.Green] = Color.FromArgb("#4CAF50"),
        [AccentTheme.Yellow] = Color.FromArgb("#F9A825"),
        [AccentTheme.Orange] = Color.FromArgb("#FB8C00"),
        [AccentTheme.Red] = Color.FromArgb("#E53935"),
        [AccentTheme.Pink] = Color.FromArgb("#D81B60"),
    };

    /// <summary>What "Device" falls back to on a platform/session where the OS accent colour
    /// can't be read (see <see cref="Services.DeviceAccentColor"/>) — the app's original purple,
    /// so a client that can't detect the OS accent looks the same as it always has rather than
    /// picking an arbitrary colour.</summary>
    public const AccentTheme DeviceFallbackAccent = AccentTheme.Purple;

    /// <summary>The seed colour for a non-<see cref="AccentTheme.Device"/> choice. Callers resolve
    /// <see cref="AccentTheme.Device"/> itself via <see cref="Services.DeviceAccentColor"/> before
    /// reaching here.</summary>
    public static Color GetAccentSeed(AccentTheme accent) =>
        AccentSeeds.TryGetValue(accent, out var color) ? color : AccentSeeds[DeviceFallbackAccent];

    /// <summary>
    /// The semantic colour set for one effective mode. Keys match exactly what AppTheme.xaml
    /// defines as static fallbacks and what every <c>DynamicResource</c> in the app looks up.
    /// </summary>
    public static Dictionary<string, Color> BuildPalette(bool dark, Color accent)
    {
        Color surface, panelBackground, divider, primaryText, secondaryText, hover,
            error, errorBackground, assistantBubble;

        if (dark)
        {
            // Matches the Windows Fluent ComboBox popup's neutral dark card surface. Keeping the
            // app's primary and panel surfaces equal prevents a conspicuous dark-purple band
            // behind an otherwise native-looking settings popup.
            surface = Color.FromArgb("#2C2C2C");
            panelBackground = surface;
            divider = Color.FromArgb("#454545");
            primaryText = Color.FromArgb("#F5F5F5");
            secondaryText = Color.FromArgb("#C4C4C4");
            hover = Color.FromArgb("#383838");
            error = Color.FromArgb("#FF8A80");
            errorBackground = Color.FromArgb("#4A2525");
            assistantBubble = Color.FromArgb("#333333");
        }
        else
        {
            surface = Color.FromArgb("#FFFFFF");
            panelBackground = surface;
            divider = Color.FromArgb("#E5E3ED");
            primaryText = Color.FromArgb("#1F1B2E");
            secondaryText = Color.FromArgb("#8A8698");
            hover = Color.FromArgb("#EFEEF4");
            error = Color.FromArgb("#D9534F");
            errorBackground = Color.FromArgb("#FDECEA");
            assistantBubble = Color.FromArgb("#F2F2F5");
        }

        // The user-message bubble tracks whichever accent is active — a light tint of it on a
        // light background, a dark tint on a dark one — rather than a fixed lavender. That's the
        // one background colour in the app that's genuinely "accent-flavoured" rather than a
        // plain neutral, so it's the one place accent choice shows up outside of buttons/icons.
        var userBubble = dark
            ? Blend(accent, Colors.Black, 0.72)
            : Blend(accent, Colors.White, 0.88);

        // Windows-style navigation selection: a restrained accent wash over the neutral panel,
        // rather than a saturated button fill. It stays readable for every allowed accent,
        // including Yellow, while clearly tying the active row to the selected Colour Theme.
        var contextRowSelectedBackground = Blend(accent, panelBackground, 0.92);

        return new Dictionary<string, Color>
        {
            ["AccentColor"] = accent,
            ["SurfaceColor"] = surface,
            ["PanelBackgroundColor"] = panelBackground,
            ["ContextRowSelectedBackgroundColor"] = contextRowSelectedBackground,
            ["DividerColor"] = divider,
            ["PrimaryTextColor"] = primaryText,
            ["SecondaryTextColor"] = secondaryText,
            ["HoverColor"] = hover,
            ["UserBubbleColor"] = userBubble,
            ["AssistantBubbleColor"] = assistantBubble,
            ["ErrorColor"] = error,
            ["ErrorBackgroundColor"] = errorBackground,
        };
    }

    /// <summary>Linear per-channel blend toward <paramref name="mixWith"/>. <paramref name="amount"/>
    /// 0 returns <paramref name="baseColor"/> unchanged, 1 returns <paramref name="mixWith"/>.</summary>
    private static Color Blend(Color baseColor, Color mixWith, double amount) => Color.FromRgba(
        baseColor.Red + (mixWith.Red - baseColor.Red) * amount,
        baseColor.Green + (mixWith.Green - baseColor.Green) * amount,
        baseColor.Blue + (mixWith.Blue - baseColor.Blue) * amount,
        1.0);
}
