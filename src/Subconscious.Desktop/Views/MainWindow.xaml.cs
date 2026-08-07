using Subconscious.Desktop.Services;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

/// <summary>The native application window. Windows owns the complete non-client title bar,
/// including dragging, snap layouts, system menus, and caption buttons.</summary>
public partial class MainWindow : Window
{
    private readonly ThemeService _theme;

    public MainWindow(MainViewModel viewModel, MainPage page, ThemeService theme)
    {
        InitializeComponent();

        BindingContext = viewModel;
        Page = page;
        _theme = theme;

        // ThemeService.Initialize runs before this window exists. HandlerChanged can still fire
        // before MAUI has added the window to Application.Windows, so Created is the reliable
        // post-creation point; later theme changes keep the native tree in sync.
        ApplyPlatformTheme();
        HandlerChanged += (_, _) => ApplyPlatformTheme();
        Created += (_, _) => ApplyPlatformTheme();
        _theme.Changed += (_, _) => ApplyPlatformTheme();
    }

    private void ApplyPlatformTheme() => PlatformTheme.Apply(_theme.IsDark);
}
