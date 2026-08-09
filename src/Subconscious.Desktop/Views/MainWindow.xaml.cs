using Subconscious.Desktop.Services;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

/// <summary>The native application window. Windows owns the complete non-client title bar,
/// including dragging, snap layouts, system menus, and caption buttons.</summary>
public partial class MainWindow : Window
{
    private readonly ThemeService _theme;
    private readonly ChatViewModel _chat;

    public MainWindow(MainViewModel viewModel, MainPage page, ThemeService theme)
    {
        InitializeComponent();

        BindingContext = viewModel;
        _chat = viewModel.Chat;
        // TitleBar.LeadingContent is hosted outside the Window's normal visual tree on WinUI.
        // Set and maintain its values imperatively so its text does not collapse when that host
        // declines to propagate a MAUI binding context.
        UpdateTitleBarStatus();
        _chat.PropertyChanged += OnChatPropertyChanged;
        Page = page;
        _theme = theme;

        // ThemeService.Initialize runs before this window exists. HandlerChanged can still fire
        // before MAUI has added the window to Application.Windows, so Created is the reliable
        // post-creation point; later theme changes keep the native tree in sync.
        ApplyPlatformTheme();
        HandlerChanged += (_, _) => ApplyPlatformTheme();
        Created += (_, _) =>
        {
            ApplyPlatformTheme();
            UpdateTitleBarStatus();
        };
        _theme.Changed += (_, _) => ApplyPlatformTheme();
    }

    private void ApplyPlatformTheme() => PlatformTheme.Apply(_theme.IsDark);

    private void OnChatPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatViewModel.TitleBarContextText)
            or nameof(ChatViewModel.StatusText)
            or nameof(ChatViewModel.IsConnected))
        {
            UpdateTitleBarStatus();
        }
    }

    private void UpdateTitleBarStatus()
    {
        TitleBarContextLabel.Text = _chat.TitleBarContextText;
        TitleBarStatusLabel.Text = _chat.StatusText;
        TitleBarConnectionIndicator.IsVisible = _chat.IsConnected;
        TitleBarStatusContent.InvalidateMeasure();
        AppTitleBar.InvalidateMeasure();
    }
}
