using Avalonia;
using Avalonia.Markup.Xaml;
using Subconscious.Desktop.Views;
using Subconscious.Desktop.ViewModels;
using Avalonia.Controls.ApplicationLifetimes;

namespace Subconscious.Desktop;

public partial class App : Application
{
    /// <summary>Set from command-line args in <c>Program.Main</c> before the app starts.</summary>
    public static bool DevMode { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = new MainWindowViewModel();
            var window = new MainWindow { DataContext = mainWindowViewModel };
            desktop.MainWindow = window;

            // Fire-and-forget: the window renders immediately with "Connecting…" status;
            // InitializeAsync updates it once the engine handshake completes.
            _ = mainWindowViewModel.Chat.InitializeAsync(DevMode);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
