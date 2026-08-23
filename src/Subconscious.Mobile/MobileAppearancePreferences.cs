namespace Subconscious.Mobile;

public enum MobileAppearancePalette { Default, Purple, Blue, Teal, Green, Yellow, Orange, Red, Pink }
public enum MobileLightingMode { System, Light, Dark }

/// <summary>Persists Mobile-only surface color and lighting preferences.</summary>
public sealed class MobileAppearancePreferences
{
    private const string PalettePreferenceKey = "subconscious.mobile.appearance.palette";
    private const string LightingPreferenceKey = "subconscious.mobile.appearance.lighting";
    private Application? _application;

    public IReadOnlyList<string> PaletteOptions { get; } = Enum.GetNames<MobileAppearancePalette>();
    public IReadOnlyList<string> LightingOptions { get; } = Enum.GetNames<MobileLightingMode>();

    public string Palette { get; private set; } = ReadPreference<MobileAppearancePalette>(PalettePreferenceKey).ToString();
    public string LightingMode { get; private set; } = ReadPreference<MobileLightingMode>(LightingPreferenceKey).ToString();

    public void Initialize(Application application)
    {
        if (_application is not null) return;
        _application = application;
        application.RequestedThemeChanged += (_, _) => ApplyResources();
        Apply();
    }

    public void SetPalette(string? palette)
    {
        Palette = Parse<MobileAppearancePalette>(palette).ToString();
        Preferences.Default.Set(PalettePreferenceKey, Palette);
        ApplyResources();
    }

    public void SetLightingMode(string? lightingMode)
    {
        LightingMode = Parse<MobileLightingMode>(lightingMode).ToString();
        Preferences.Default.Set(LightingPreferenceKey, LightingMode);
        Apply();
    }

    private void Apply()
    {
        if (_application is null) return;
        _application.UserAppTheme = Parse<MobileLightingMode>(LightingMode) switch
        {
            MobileLightingMode.Light => AppTheme.Light,
            MobileLightingMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
        ApplyResources();
    }

    public event EventHandler? AppearanceChanged;

    private void ApplyResources()
    {
        if (_application is null) return;
        var palette = Parse<MobileAppearancePalette>(Palette);
        var dark = _application.RequestedTheme == AppTheme.Dark;
        var definition = palette switch
        {
            MobileAppearancePalette.Purple => ("#F5F1FB", "#673AB7"),
            MobileAppearancePalette.Blue => ("#EFF7FE", "#2196F3"),
            MobileAppearancePalette.Teal => ("#E6F5F3", "#009688"),
            MobileAppearancePalette.Green => ("#EEF8EE", "#4CAF50"),
            MobileAppearancePalette.Yellow => ("#FFF9E6", "#F9A825"),
            MobileAppearancePalette.Orange => ("#FFF5E8", "#FB8C00"),
            MobileAppearancePalette.Red => ("#FCEDEC", "#E53935"),
            MobileAppearancePalette.Pink => ("#FCEAF2", "#D81B60"),
            _ => ("#FFFFFF", dark ? "#FFFFFF" : "#000000"),
        };
        var surface = Color.FromArgb(dark ? "#2C2C2C" : definition.Item1);
        var accent = Color.FromArgb(definition.Item2);
        var onAccent = palette switch
        {
            MobileAppearancePalette.Default when dark => Colors.Black,
            MobileAppearancePalette.Yellow => Color.FromArgb("#1F1B2E"),
            _ => Colors.White,
        };
        _application.Resources["MobileSurfaceColor"] = surface;
        _application.Resources["MobilePrimaryTextColor"] = Color.FromArgb(dark ? "#F5F5F5" : "#1F1B2E");
        _application.Resources["MobileSecondaryTextColor"] = Color.FromArgb(dark ? "#C4C4C4" : "#8A8698");
        _application.Resources["MobileDividerColor"] = Blend(accent, surface, dark ? 0.78 : 0.86);
        _application.Resources["MobileAccentColor"] = accent;
        _application.Resources["MobileOnAccentColor"] = onAccent;
        _application.Resources["MobileContextHighlightColor"] = Blend(accent, surface, dark ? 0.72 : 0.88);

        // NativeChatTranscriptView consumes Desktop-compatible semantic keys. Supplying them
        // here prevents its built-in purple fallbacks from bypassing Mobile's selected palette.
        _application.Resources["AccentColor"] = accent;
        _application.Resources["PrimaryTextColor"] = _application.Resources["MobilePrimaryTextColor"];
        _application.Resources["SecondaryTextColor"] = _application.Resources["MobileSecondaryTextColor"];
        _application.Resources["UserBubbleColor"] = Blend(accent, dark ? Colors.Black : Colors.White, dark ? 0.72 : 0.88);
        _application.Resources["AssistantBubbleColor"] = Color.FromArgb(dark ? "#333333" : "#F2F2F5");
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Color Blend(Color baseColor, Color mixWith, double amount) => Color.FromRgba(
        baseColor.Red + (mixWith.Red - baseColor.Red) * amount,
        baseColor.Green + (mixWith.Green - baseColor.Green) * amount,
        baseColor.Blue + (mixWith.Blue - baseColor.Blue) * amount,
        1.0);

    private static T ReadPreference<T>(string key) where T : struct, Enum =>
        Parse<T>(Preferences.Default.Get(key, default(T).ToString()));

    private static T Parse<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : default;
}
