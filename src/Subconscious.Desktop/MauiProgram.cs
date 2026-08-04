using MauiIcons.Fluent;
using Microsoft.Extensions.Logging;
using Subconscious.Desktop.Services;
using Subconscious.Desktop.ViewModels;
using Subconscious.Desktop.Views;

namespace Subconscious.Desktop;

public static class MauiProgram
{
    /// <summary>True when the app was launched with <c>--dev</c>, which points engine discovery
    /// at the <c>-dev</c> data directory. MAUI has no <c>Main(string[] args)</c> of its own on
    /// Windows, so the command line is read here instead of an entry point.</summary>
    public static bool DevMode { get; private set; }

    public static MauiApp CreateMauiApp()
    {
        DevMode = Environment.GetCommandLineArgs().Contains("--dev");
        Diagnostics.CrashLog.DevMode = DevMode;
        Diagnostics.CrashLog.Install();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseFluentMauiIcons()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // One layout store and one root view model for the whole app. MainPage is resolved
        // through DI too so it gets the same MainViewModel instance the store was loaded into.
        builder.Services.AddSingleton(_ => new LayoutStateStore(DevMode));
        builder.Services.AddSingleton(_ => new ThemeStateStore(DevMode));
        builder.Services.AddSingleton<PanelConfigurationStore>();
        builder.Services.AddSingleton<DesktopUiStateStore>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();
        // Transient: a Window instance can't be handed to a second CreateWindow call, so the
        // window is rebuilt per activation while the page and view model behind it persist.
        builder.Services.AddTransient<MainWindow>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
