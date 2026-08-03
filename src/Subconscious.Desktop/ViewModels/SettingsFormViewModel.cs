using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Resources.Styles;
using Subconscious.Desktop.Services;

namespace Subconscious.Desktop.ViewModels;

/// <summary>
/// Drives the center panel's General Settings form, opened from the Settings context-panel
/// section's "General Settings" row.
///
/// <see cref="Mode"/> and <see cref="ColourTheme"/> read from and write straight to the injected
/// <see cref="ThemeService"/> — there's no separate "save" step; picking an item in either Picker
/// applies and persists it immediately, the same way the OS's own light/dark and accent-colour
/// settings work. "Device" is a real, selectable option in both lists rather than a separate
/// switch: picking it means "don't store an explicit value, follow the OS setting".
///
/// This does not yet touch the engine's <c>app_state</c> table (see
/// <c>tests/Subconscious.Engine.Tests/app_state.txt</c>'s <c>mode</c>/<c>colour</c> rows) — there
/// is no HTTP endpoint for it. <see cref="ThemeService"/> persists to a local file instead (see
/// <see cref="ThemeStateStore"/>); moving that to the engine later doesn't change this class.
/// </summary>
public sealed partial class SettingsFormViewModel : ViewModelBase
{
    private readonly ThemeService _theme;

    /// <summary>Picker options for <see cref="Mode"/>.</summary>
    public IList<ThemeMode> ModeOptions { get; } = Enum.GetValues<ThemeMode>();

    /// <summary>Picker options for <see cref="ColourTheme"/>, matching <c>colours.txt</c>'s seed
    /// list plus the "Device" entry (see <see cref="AccentTheme"/>).</summary>
    public IList<AccentTheme> ColourThemeOptions { get; } = Enum.GetValues<AccentTheme>();

    /// <summary>Where the engine reads/writes its data — same value as
    /// <see cref="MainViewModel.DataDirectory"/>, passed in rather than re-resolved here so there's
    /// one place that knows how to find it.</summary>
    public string DataDirectory { get; }

    [ObservableProperty]
    private ThemeMode _mode;

    [ObservableProperty]
    private AccentTheme _colourTheme;

    /// <summary>
    /// Mirrors the app's current <c>PrimaryTextColor</c> resource, refreshed on every
    /// <see cref="ThemeService.Changed"/>. Exists so the Data Folder row's copy/open icons can bind
    /// to it via <c>IconColor={Binding IconColor}</c> nested inside the <c>mi:Fluent</c> markup
    /// extension.
    ///
    /// Why not <c>IconColor="{DynamicResource PrimaryTextColor}"</c> directly: that only resolves
    /// correctly when it's the value of a real XAML attribute or property-element on a
    /// <see cref="BindableObject"/> that the compiler processes as such. Nested inside another
    /// markup extension's own property list (<c>{mi:Fluent Icon=X, IconColor={DynamicResource Y}}</c>)
    /// it isn't — <c>DynamicResourceExtension.ProvideValue</c> returns a <c>DynamicResource</c>
    /// token, not a <c>Color</c>, and there's no compiled <c>SetDynamicResource</c> call for a
    /// property nested this way, so the assignment silently fails and <c>IconColor</c> stays at its
    /// default (rendering white/unthemed on WinUI regardless of the active theme — the bug this
    /// property exists to fix). MauiIcons' own docs show nesting <c>{Binding ...}</c> the same way
    /// and it does work there, because a <c>Binding</c> is a live object the extension can attach
    /// as-is; a plain <see cref="Color"/> from this property behaves the same way.
    /// </summary>
    [ObservableProperty]
    private Color _iconColor;

    private readonly EventHandler _onThemeChanged;

    /// <summary>Raised when the form's Close button is pressed, so the host (MainViewModel) can
    /// hide the panel without this view model needing to know it's hosted in one.</summary>
    public event EventHandler? Closed;

    public SettingsFormViewModel(ThemeService theme, string dataDirectory)
    {
        _theme = theme;
        DataDirectory = dataDirectory;
        _mode = theme.Mode;
        _colourTheme = theme.Accent;
        _iconColor = ResolveIconColor();

        // Kept as a field (rather than an inline lambda passed straight to +=) so Detach can
        // unsubscribe the exact same delegate — ThemeService is a long-lived singleton, and a new
        // SettingsFormViewModel is created every time the form is opened, so an un-detached
        // subscription here would leak one handler (and this whole view model) per open.
        _onThemeChanged = (_, _) => IconColor = ResolveIconColor();
        _theme.Changed += _onThemeChanged;
    }

    private static Color ResolveIconColor() =>
        Application.Current?.Resources.TryGetValue("PrimaryTextColor", out var value) == true && value is Color color
            ? color
            : Colors.Black;

    partial void OnModeChanged(ThemeMode value) => _theme.SetMode(value);

    partial void OnColourThemeChanged(AccentTheme value) => _theme.SetAccent(value);

    /// <summary>Unsubscribes from <see cref="ThemeService.Changed"/> — called by
    /// <c>MainViewModel.CloseSettingsForm</c> alongside detaching <see cref="Closed"/>, since this
    /// view model is discarded (not reused) the next time the form opens.</summary>
    public void Detach() => _theme.Changed -= _onThemeChanged;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>Copies <see cref="DataDirectory"/> to the clipboard.</summary>
    [RelayCommand]
    private Task CopyDataDirectoryAsync() => Clipboard.Default.SetTextAsync(DataDirectory);

    /// <summary>Opens <see cref="DataDirectory"/> in the OS file browser (Explorer on Windows,
    /// Finder on Mac Catalyst) via <see cref="Launcher"/> — the one cross-platform MAUI API for
    /// "hand a path to the system", rather than shelling out to <c>explorer.exe</c> directly.</summary>
    [RelayCommand]
    private Task OpenDataDirectoryAsync() => Launcher.Default.OpenAsync(new Uri(DataDirectory));
}
