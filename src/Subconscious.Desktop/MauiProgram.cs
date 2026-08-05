using MauiIcons.Fluent;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.Maui.Handlers;
using Subconscious.Desktop.Controls;
#endif
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
#if WINDOWS
            // The chat composer owns its outline through ChatPanelView's Border. Removing the
            // inner WinUI TextBox border avoids a doubled field while all other Editors retain
            // their normal native form chrome.
            .ConfigureMauiHandlers(_ =>
            {
                EditorHandler.Mapper.AppendToMapping(nameof(ChatComposerEditor), (handler, view) =>
                {
                    if (view is not ChatComposerEditor)
                    {
                        return;
                    }

                    // TextBox replaces its zero-width base border with a focused bottom rule via
                    // theme resources. Override both states locally so the omnibox Border remains
                    // the only field chrome, including while this native editor has focus.
                    var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    var noBorder = new Microsoft.UI.Xaml.Thickness(0);
                    handler.PlatformView.BorderBrush = transparent;
                    handler.PlatformView.BorderThickness = noBorder;
                    handler.PlatformView.Resources["TextControlBorderBrush"] = transparent;
                    handler.PlatformView.Resources["TextControlBorderBrushPointerOver"] = transparent;
                    handler.PlatformView.Resources["TextControlBorderBrushFocused"] = transparent;
                    handler.PlatformView.Resources["TextControlBorderThemeThickness"] = noBorder;
                    handler.PlatformView.Resources["TextControlBorderThemeThicknessFocused"] = noBorder;
                });
            })
#endif
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
        builder.Services.AddSingleton<SidebarPositionStore>();
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
