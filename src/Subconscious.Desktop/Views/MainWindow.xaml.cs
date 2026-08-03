using Subconscious.Desktop.Services;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

/// <summary>
/// The app window. Exists as a type of its own so the custom <see cref="TitleBar"/> can be
/// declared in XAML (see MainWindow.xaml) rather than assembled by hand in
/// <see cref="App.CreateWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ThemeService _theme;

    public MainWindow(MainViewModel viewModel, MainPage page, ThemeService theme)
    {
        InitializeComponent();

        // The title bar binds to the same view model as the page, so its context panel toggle and
        // the panel it shows stay in step. Bindings on a TitleBar resolve against the Window's
        // BindingContext, not the Page's.
        BindingContext = viewModel;
        Page = page;

        _theme = theme;

        // AppThemeBinding on TitleBar.Icon selects the matching processed image asset as
        // UserAppTheme changes. The native window theme still needs explicit WinUI handling.
        ApplyPlatformTheme();

        // ThemeService.Initialize (called from App's constructor) runs before this window exists,
        // so its first PlatformTheme.Apply call had no native window to reach. Re-apply once this
        // window's native handler is actually available, and again on every later theme change —
        // PlatformTheme.Apply only ever touches windows that currently exist in
        // Application.Current.Windows, so a change made before this fires would otherwise never
        // reach this window's native Fluent theme resources.
        HandlerChanged += (_, _) => ApplyPlatformTheme();
        _theme.Changed += (_, _) => ApplyPlatformTheme();
    }

    private void ApplyPlatformTheme()
    {
        PlatformTheme.Apply(_theme.IsDark);
    }
}
