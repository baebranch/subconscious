using Subconscious.Desktop.Resources.Styles;

namespace Subconscious.Desktop.Services;

/// <summary>
/// Owns the app's live theme: loads the persisted choice on startup, applies it to
/// <c>Application.Current.Resources</c> so every <c>DynamicResource</c> in the app repaints, and
/// re-applies it whenever the mode or accent changes or the OS theme changes underneath a
/// "Device" mode setting.
///
/// One instance for the app's lifetime, resolved through DI (see <c>MauiProgram</c>) rather than
/// static, so it can hold the <see cref="ThemeStateStore"/> it loaded from and be handed to
/// <see cref="ViewModels.SettingsFormViewModel"/> without a service locator.
/// </summary>
public sealed class ThemeService
{
    private readonly ThemeStateStore _store;
    private ThemeState _state;

    public ThemeService(ThemeStateStore store)
    {
        _store = store;
        _state = store.Load();
    }

    public ThemeMode Mode => _state.Mode;

    public AccentTheme Accent => _state.Accent;

    /// <summary>The effective light/dark this service last resolved Mode to — Device already
    /// resolved against the OS. <see cref="Views.MainWindow"/> reads this to (re)apply the native
    /// window theme once its handler exists and on every subsequent <see cref="Changed"/>.</summary>
    public bool IsDark { get; private set; }

    /// <summary>Raised after every re-apply (a mode/accent change, or the OS theme flipping under
    /// a Device setting) so bound UI (e.g. a Picker's SelectedItem) can stay in sync without
    /// polling.</summary>
    public event EventHandler? Changed;

    /// <summary>Loads the persisted theme, paints it, and starts listening for OS theme changes.
    /// Called once from <c>App</c> — after <c>InitializeComponent</c> has merged AppTheme.xaml
    /// (see <c>App.xaml.cs</c>'s note on <c>CreateWindow</c>), so there's a base resource
    /// dictionary already in place for the first <c>Apply()</c> to overwrite keys in.</summary>
    public void Initialize()
    {
        Apply();

        // Only matters while Mode == Device: Apply() below re-checks Mode every time, so a
        // Light/Dark selection makes this a no-op rather than needing to be unsubscribed.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += (_, _) => Apply();
        }
    }

    public void SetMode(ThemeMode mode)
    {
        if (_state.Mode == mode)
        {
            return;
        }
        _state.Mode = mode;
        _store.Save(_state);
        Apply();
    }

    public void SetAccent(AccentTheme accent)
    {
        if (_state.Accent == accent)
        {
            return;
        }
        _state.Accent = accent;
        _store.Save(_state);
        Apply();
    }

    /// <summary>Resolves Device → an actual light/dark + accent colour, builds the palette, and
    /// writes every key straight into <c>Application.Current.Resources</c> — the same dictionary
    /// AppTheme.xaml's static values live in, so a <c>DynamicResource</c> lookup finds this
    /// instead the moment it's set, and a plain <c>StaticResource</c> lookup (anything not yet
    /// converted) still finds AppTheme.xaml's originals underneath, just non-reactive.</summary>
    private void Apply()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var dark = _state.Mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => app.RequestedTheme == AppTheme.Dark,
        };
        IsDark = dark;

        var accentColor = _state.Accent == AccentTheme.Device
            ? DeviceAccentColor.Get() ?? ThemePalette.GetAccentSeed(ThemePalette.DeviceFallbackAccent)
            : ThemePalette.GetAccentSeed(_state.Accent);

        foreach (var (key, color) in ThemePalette.BuildPalette(dark, accentColor))
        {
            app.Resources[key] = color;
        }

        // The three Style-level Brush aliases (AppTheme.xaml's AccentBrush/DividerBrush/
        // SurfaceBrush) wrap a Color in a SolidColorBrush for controls that take a Brush, not a
        // Color (Border.Stroke). They have to be rebuilt here too: a Brush created earlier still
        // points at the Color object it was constructed with, not at the resource *key*, so
        // overwriting AccentColor above doesn't retroactively change a brush that already copied
        // its value out.
        app.Resources["AccentBrush"] = new SolidColorBrush(accentColor);
        app.Resources["DividerBrush"] = new SolidColorBrush((Color)app.Resources["DividerColor"]);
        app.Resources["SurfaceBrush"] = new SolidColorBrush((Color)app.Resources["SurfaceColor"]);

        // Keeps native controls (Picker's popup, Entry's caret/selection, scrollbars) matching:
        // this is the one setting MAUI does expose a supported cross-platform API for (unlike the
        // OS accent colour), or the docs note there's no supported way to force it.
        app.UserAppTheme = dark ? AppTheme.Dark : AppTheme.Light;

        // UserAppTheme alone doesn't reliably repaint controls that already exist and carry no
        // explicit style — WinUI's Fluent theme resources for them key off the native window's own
        // RequestedTheme, and that doesn't automatically follow UserAppTheme once the window has
        // already been created. Without this, the Settings form's unstyled Picker/Entry controls
        // (left native on purpose — see AppTheme.xaml's "Settings form" region) keep rendering
        // Dark-theme text (white) if the real OS theme is Dark while Light is selected here, which
        // is invisible against this app's Light surface.
        //
        // Not called directly from here: this method can run before any window exists (the very
        // first Initialize() call, from App's constructor), and PlatformTheme.Apply only reaches
        // windows that already exist. MainWindow instead applies it once its own handler is ready,
        // and again on every Changed below.
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
