using Microsoft.UI.Xaml;

namespace Subconscious.Desktop.WinUI;

/// <summary>The WinUI head. Everything app-specific lives in <see cref="MauiProgram"/>.</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        // WinUI routes startup/XAML failures here rather than through
        // AppDomain.UnhandledException, and an unpackaged app that throws simply vanishes without
        // a dialog or an event log entry — so this is the only place such a failure is visible.
        UnhandledException += (_, e) => Diagnostics.CrashLog.Write(e.Exception);

        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp()
    {
        try
        {
            return MauiProgram.CreateMauiApp();
        }
        catch (Exception ex)
        {
            Diagnostics.CrashLog.Write(ex);
            throw;
        }
    }
}
