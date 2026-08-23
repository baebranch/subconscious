using MauiIcons.Fluent;
using Microsoft.Extensions.Logging;
#if WINDOWS
using Microsoft.Maui.Handlers;
using Subconscious.Desktop.Controls;
#endif
using Subconscious.Desktop.Services;
using Subconscious.Desktop.ViewModels;
using Subconscious.Desktop.Views;
using Subconscious.WYSIWYG;

namespace Subconscious.Desktop;

public static class MauiProgram
{
    /// <summary>True when the app was launched with <c>--dev</c>, which points engine discovery
    /// at the <c>-dev</c> data directory. MAUI has no <c>Main(string[] args)</c> of its own on
    /// Windows, so the command line is read here instead of an entry point.</summary>
    public static bool DevMode { get; private set; }

    /// <summary>True for Debug builds and runtime <c>--dev</c> launches, which should expose
    /// development-only visual indicators.</summary>
    public static bool ShowDevelopmentIndicators => DevMode || IsDebugBuild;

    private static bool IsDebugBuild =>
#if DEBUG
        true;
#else
        false;
#endif

    public static MauiApp CreateMauiApp()
    {
        DevMode = Environment.GetCommandLineArgs().Contains("--dev");
        Diagnostics.CrashLog.DevMode = DevMode;
        Diagnostics.CrashLog.Install();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseFluentMauiIcons()
            .UseSubconsciousWysiwyg()
#if WINDOWS
            // The chat composer owns its outline through ChatPanelView's Border. Removing the
            // inner WinUI TextBox border avoids a doubled field while all other Editors retain
            // their normal native form chrome.
            .ConfigureMauiHandlers(_ =>
            {
                EditorHandler.Mapper.AppendToMapping(nameof(ChatComposerEditor), (handler, view) =>
                {
                    if (view is not ChatComposerEditor composer)
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

                    // A multiline WinUI TextBox treats both key combinations as newline by
                    // default. Consume plain Enter and invoke the bound send command; leave
                    // Shift+Enter untouched so users can deliberately insert a line break.
                    handler.PlatformView.PreviewKeyDown += (_, args) =>
                    {
                        if (args.Key != Windows.System.VirtualKey.Enter)
                        {
                            return;
                        }

                        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                            Windows.System.VirtualKey.Shift);
                        if ((shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0)
                        {
                            return;
                        }

                        args.Handled = true;
                        if (composer.SubmitCommand?.CanExecute(null) == true)
                        {
                            composer.SubmitCommand.Execute(null);
                        }
                    };
                });

                PickerHandler.Mapper.AppendToMapping(nameof(CaptionWorkspacePicker), (handler, view) =>
                {
                    if (view is not CaptionWorkspacePicker)
                    {
                        return;
                    }

                    // This picker shares its surface with the extended native caption, so it
                    // deliberately has no form-field outline in its normal, hover, or focus state.
                    var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    var noBorder = new Microsoft.UI.Xaml.Thickness(0);
                    // Use only the ComboBox's default-state resource. Setting Background directly
                    // would take precedence over WinUI's hover, focused, and pressed state setters.
                    var appResources = Microsoft.UI.Xaml.Application.Current.Resources;
                    if (!appResources.TryGetValue("CaptionWorkspacePickerBackgroundBrush", out var resource)
                        || resource is not Microsoft.UI.Xaml.Media.SolidColorBrush captionBackground)
                    {
                        captionBackground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Windows.UI.Color.FromArgb(255, 255, 255, 255));
                        appResources["CaptionWorkspacePickerBackgroundBrush"] = captionBackground;
                    }

                    handler.PlatformView.Resources["ComboBoxBackground"] = captionBackground;
                    handler.PlatformView.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
                    handler.PlatformView.Resources["ComboBoxCornerRadius"] = new Microsoft.UI.Xaml.CornerRadius(0);
                    handler.PlatformView.BorderBrush = transparent;
                    handler.PlatformView.BorderThickness = noBorder;
                    handler.PlatformView.Resources["ComboBoxBorderBrush"] = transparent;
                    handler.PlatformView.Resources["ComboBoxBorderBrushPointerOver"] = transparent;
                    handler.PlatformView.Resources["ComboBoxBorderBrushFocused"] = transparent;
                    handler.PlatformView.Resources["ComboBoxBorderThemeThickness"] = noBorder;
                    handler.PlatformView.Resources["ComboBoxBorderThemeThicknessFocused"] = noBorder;
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
