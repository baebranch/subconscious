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
        // TitleBar content is hosted outside the Window's normal visual tree on WinUI. Set and
        // maintain its values imperatively so text and the workspace selector work even when that
        // host declines to propagate a MAUI binding context.
        TitleBarWorkspacePicker.ItemsSource = _chat.WorkspaceSelectorItems;
        UpdateTitleBarStatus();
        UpdateTitleBarWorkspacePicker();
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

        if (e.PropertyName is nameof(ChatViewModel.CurrentWorkspaceSelector))
        {
            UpdateTitleBarWorkspacePicker();
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

    private bool _isUpdatingTitleBarWorkspacePicker;

    private void UpdateTitleBarWorkspacePicker()
    {
        _isUpdatingTitleBarWorkspacePicker = true;
        try
        {
            TitleBarWorkspacePicker.SelectedItem = _chat.CurrentWorkspaceSelector;
        }
        finally
        {
            _isUpdatingTitleBarWorkspacePicker = false;
        }
    }

    private async void OnTitleBarWorkspacePickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingTitleBarWorkspacePicker
            || sender is not Picker { SelectedItem: WorkspaceSelectorItem selected })
        {
            return;
        }

        try
        {
            if (selected.Workspace is null)
            {
                if (_chat.CurrentWorkspace is not null)
                {
                    await _chat.ClearWorkspaceSelectionAsync();
                }
                return;
            }

            if (selected.Workspace.Uuid != _chat.CurrentWorkspace?.Uuid)
            {
                await _chat.SelectWorkspaceAsync(selected.Workspace);
            }
        }
        catch (Exception ex)
        {
            _chat.StatusText = $"Couldn't open workspace: {ex.Message}";
            UpdateTitleBarWorkspacePicker();
        }
    }
}
