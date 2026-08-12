using System.Diagnostics;

namespace Subconscious.WYSIWYG;

/// <summary>
/// Opt-in tracing for editor layout and load timing. Enabled only when the
/// SUBC_WYSIWYG_DIAG environment variable is set to 1, and writes nothing otherwise.
/// </summary>
internal static class EditorDiagnostics
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SUBC_WYSIWYG_DIAG") == "1";

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "wysiwyg-diag.log");

    public static bool IsEnabled => Enabled;

    public static void Log(string message)
    {
        if (!Enabled) return;
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    public static Stopwatch? Start() => Enabled ? Stopwatch.StartNew() : null;

    public static void Stop(Stopwatch? stopwatch, string message)
    {
        if (stopwatch is null) return;
        stopwatch.Stop();
        Log($"{message} took {stopwatch.Elapsed.TotalMilliseconds:F1}ms");
    }
}
