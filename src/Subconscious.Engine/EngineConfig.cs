namespace Subconscious.Engine;

/// <summary>
/// Engine startup configuration. Mirrors the Python <c>Config</c> dataclass in
/// <c>config.py</c>. Secrets/encryption-at-rest, YAML persistence, and the peer-approval
/// sub-config are intentionally not yet implemented here — see translation.md §4.2 /
/// §9 ("Phase 1 — secrets-at-rest" and "config file format" open decisions) before
/// building those out.
/// </summary>
/// <param name="Dev">
/// Development mode. When true, the effective data directory gets a "-dev" suffix so
/// dev runs never touch a real user's data (parity with <c>Config.__post_init__</c>).
/// </param>
/// <param name="Api">Whether the local loopback API should be started.</param>
/// <param name="Gui">Whether a desktop GUI host is attached (set by the CLI subcommand).</param>
/// <param name="Tui">Whether a TUI host is attached (set by the CLI subcommand).</param>
/// <param name="Headless">
/// Run without the desktop GUI window (once one exists — Phase 6; a no-op today). The
/// system tray icon is <em>not</em> affected by this flag and is always shown on
/// platforms that support one, regardless of <see cref="Headless"/> — it's how a
/// headless run stays reachable ("open Subconscious", "exit"). This flag has no direct
/// Python-side equivalent: the Python CLI's <c>engine</c> subcommand had no UI presence
/// of any kind, whereas the .NET engine always shows a tray icon when available.
/// </param>
/// <param name="Port">
/// Loopback port the local API listens on. <c>0</c> (the default) asks the OS for any
/// free port, mirroring the Python engine's dynamic-port + <c>runtime.json</c> discovery
/// model — clients never need a hardcoded port.
/// </param>
public sealed record EngineConfig(
    bool Dev = false,
    bool Api = true,
    bool Gui = false,
    bool Tui = false,
    bool Headless = false,
    int Port = 0)
{
    /// <summary>Per-run node identity. Loaded from / persisted to config.yaml in Python.</summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// Root directory for the engine's database, secrets, and runtime discovery file.
    /// Mirrors <c>Config.get_default_data_dir()</c>: <c>%APPDATA%\Subconscious</c> on
    /// Windows (dev builds get a "-dev" suffix), <c>~/Library/Application Support/Subconscious</c>
    /// on macOS, and <c>$XDG_CONFIG_HOME/subconscious</c> (or <c>~/.config/subconscious</c>) on Linux.
    /// </summary>
    public string DataDirectory => GetDataDirectory(Dev);

    private static string GetDataDirectory(bool dev)
    {
        string baseDir;
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetEnvironmentVariable("APPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            baseDir = Path.Combine(appData, "Subconscious");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support", "Subconscious");
        }
        else
        {
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = string.IsNullOrEmpty(xdgConfig)
                ? Path.Combine(home, ".config", "subconscious")
                : Path.Combine(xdgConfig, "subconscious");
        }

        return dev ? baseDir + "-dev" : baseDir;
    }
}
