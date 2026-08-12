using Subconscious.Desktop.Services;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

/// <summary>The native application window. Windows owns the complete non-client title bar,
/// including dragging, snap layouts, system menus, and caption buttons.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ThemeService _theme;
    private readonly ChatViewModel _chat;

#if WINDOWS
    private const int MinimumRestoredWindowWidth = 960;
    private const int MinimumRestoredWindowHeight = 600;

    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private DesktopWindowPlacement? _normalWindowBounds;
    private bool _desktopStateRestored;
    private bool _isRestoringWindowPlacement;
#endif

    public MainWindow(MainViewModel viewModel, MainPage page, ThemeService theme)
    {
        InitializeComponent();

        _viewModel = viewModel;
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
        Created += OnWindowCreated;
        Destroying += OnWindowDestroying;
        _viewModel.DesktopStateRestored += OnDesktopStateRestored;
        _theme.Changed += (_, _) => ApplyPlatformTheme();
    }

    private void OnWindowCreated(object? sender, EventArgs e)
    {
        ApplyPlatformTheme();
        UpdateTitleBarStatus();

#if WINDOWS
        if (Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
        {
            _appWindow = nativeWindow.AppWindow;
            _appWindow.Changed += (_, _) => CaptureWindowPlacement();
            CaptureWindowPlacement();
            RestoreWindowPlacementIfReady();
        }
#endif
    }

    private void OnDesktopStateRestored(object? sender, EventArgs e)
    {
#if WINDOWS
        _desktopStateRestored = true;
        RestoreWindowPlacementIfReady();
#endif
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
#if WINDOWS
        CaptureWindowPlacement();
#endif
        _viewModel.PersistDesktopStateOnExit();
    }

#if WINDOWS
    /// <summary>Restores normal bounds before maximizing so Windows retains the right restore size.</summary>
    private void RestoreWindowPlacementIfReady()
    {
        if (!_desktopStateRestored || _appWindow is null || _viewModel.WindowPlacement is not { } placement
            || !IsRestorable(placement))
        {
            return;
        }

        _normalWindowBounds = placement with { IsMaximized = false };
        _isRestoringWindowPlacement = true;
        try
        {
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height));

            if (placement.IsMaximized
                && _appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch (Exception)
        {
            // Startup continues with MAUI's default placement if the saved monitor/bounds are unusable.
        }
        finally
        {
            _isRestoringWindowPlacement = false;
        }
    }

    /// <summary>Uses native pixels consistently, preserving normal bounds while the window is maximized.</summary>
    private void CaptureWindowPlacement()
    {
        if (_appWindow is null || _isRestoringWindowPlacement)
        {
            return;
        }

        var isMaximized = _appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
        {
            State: Microsoft.UI.Windowing.OverlappedPresenterState.Maximized,
        };
        var position = _appWindow.Position;
        var size = _appWindow.Size;
        if (!isMaximized && size.Width >= MinimumRestoredWindowWidth && size.Height >= MinimumRestoredWindowHeight)
        {
            _normalWindowBounds = new DesktopWindowPlacement(position.X, position.Y, size.Width, size.Height, false);
        }

        if (_desktopStateRestored && _normalWindowBounds is { } normalBounds)
        {
            _viewModel.UpdateWindowPlacement(normalBounds with { IsMaximized = isMaximized });
        }
    }

    /// <summary>Never restore a malformed size or a rectangle wholly outside the available displays.</summary>
    private static bool IsRestorable(DesktopWindowPlacement placement)
    {
        if (placement.Width < MinimumRestoredWindowWidth || placement.Height < MinimumRestoredWindowHeight)
        {
            return false;
        }

        try
        {
            var display = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
                new Windows.Graphics.PointInt32(placement.X, placement.Y),
                Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;
            return placement.X < (long)workArea.X + workArea.Width
                && (long)placement.X + placement.Width > workArea.X
                && placement.Y < (long)workArea.Y + workArea.Height
                && (long)placement.Y + placement.Height > workArea.Y;
        }
        catch (Exception)
        {
            return false;
        }
    }
#endif

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
