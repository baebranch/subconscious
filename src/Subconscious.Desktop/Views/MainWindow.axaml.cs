using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;


namespace Subconscious.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Keep the maximize/restore glyph in sync when the window state changes via
        // double-click on the titlebar, Windows snap, or the taskbar.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMaximizeRestoreGlyph();
            }
        };
    }

    /// <summary>
    /// Drag-to-move for the custom titlebar. Also handles maximize/restore on
    /// double-click, matching standard OS titlebar behavior.
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ToggleMaximizeRestore();

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void ToggleMaximizeRestore() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaximizeRestoreGlyph()
    {
        if (MaximizeRestoreButton is null)
        {
            return;
        }

        // Segoe MDL2 Assets: E922 = maximize, E923 = restore.
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.SetValue(ToolTip.TipProperty, WindowState == WindowState.Maximized ? "Restore" : "Maximize");
    }
}
