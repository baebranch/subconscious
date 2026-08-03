using Microsoft.Extensions.DependencyInjection;
using Subconscious.Desktop.Services;
using Subconscious.Desktop.Views;

namespace Subconscious.Desktop;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;

        // Paints the persisted (or Device-derived) theme into Application.Resources before any
        // page is built. Has to run after InitializeComponent — it overwrites keys AppTheme.xaml
        // just merged in — and before CreateWindow resolves MainWindow, so the first frame is
        // already correct instead of flashing the fallback purple/light values and then
        // repainting. UserAppTheme is no longer pinned to Light here: ThemeService.Initialize sets
        // it to whatever the resolved mode actually is (Device included), and keeps it in sync
        // itself on every later change.
        _services.GetRequiredService<ThemeService>().Initialize();
    }

    // MainWindow (and the MainPage it pulls in) is resolved here rather than injected into the
    // constructor on purpose: constructor arguments are built before this type's own constructor
    // body runs, so an injected window would parse its XAML before InitializeComponent() has
    // merged AppTheme.xaml into Application.Resources - every StaticResource in the window and
    // page would fail to resolve.
    //
    // Size and the custom title bar are declared in MainWindow.xaml.
    protected override Window CreateWindow(IActivationState? activationState) =>
        _services.GetRequiredService<MainWindow>();
}
