using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.Diagnostics;

/// <summary>
/// Appends unhandled exceptions to <c>desktop-crash.log</c> in the engine data directory.
///
/// This exists because of how an unpackaged WinUI app fails: it exits with no dialog, no console
/// output (the process is a GUI subsystem binary, so stdout goes nowhere) and no event log entry.
/// Without a log there is nothing at all to diagnose from.
/// </summary>
public static class CrashLog
{
    /// <summary>Set from <see cref="MauiProgram"/> so the log lands beside the data directory the
    /// rest of the app is using (<c>-dev</c> included).</summary>
    public static bool DevMode { get; set; }

    public static void Write(object? error)
    {
        try
        {
            var path = Path.Combine(EngineDiscovery.DataDirectory(DevMode), "desktop-crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nothing useful left to do if even the crash log can't be written.
        }
    }

    /// <summary>Hooks the runtime-level handlers. WinUI's own
    /// <c>Application.UnhandledException</c> is wired up separately in the Windows head, since it
    /// catches things these two never see.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception);
            e.SetObserved();
        };
    }
}
