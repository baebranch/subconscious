using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Subconscious.Desktop.Engine;

/// <summary>
/// Locates (and, if necessary, starts) a running Subconscious engine on this machine.
/// Direct port of <c>subconscious-code/src/engine/discovery.ts</c>'s approach: read
/// <c>runtime.json</c> from the well-known data directory, probe <c>/api/v1/health</c> to
/// confirm it's actually alive (a stale file from a crashed engine is otherwise
/// indistinguishable from a live one), and spawn <c>subconscious engine</c> if nothing
/// answers.
/// </summary>
public static class EngineDiscovery
{
    private const string RuntimeFileName = "runtime.json";
    // EngineHost deliberately pins development to this loopback endpoint/token so local debug
    // clients can still recover when an interrupted dev shutdown leaves no runtime metadata.
    // Production never uses this fallback; it continues to require its per-run runtime token.
    private const string DevelopmentHost = "127.0.0.1";
    private const int DevelopmentPort = 55681;
    private const string DevelopmentToken = "subconscious-dev-token";
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>The data directory the engine reads/writes for the given dev mode, mirroring <c>EngineConfig.DataDirectory</c>.</summary>
    public static string DataDirectory(bool dev)
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
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".config", "subconscious") : Path.Combine(xdg, "subconscious");
        }
        return dev ? baseDir + "-dev" : baseDir;
    }

    /// <summary>
    /// Find a reachable Engine within the data directory for the requested mode, starting one if
    /// none is running and <paramref name="autoStart"/> is true. Dev and production clients are
    /// deliberately isolated: mixing their runtime files can make the picker read configurations
    /// from one encrypted store while chat executes against another Engine process.
    /// </summary>
    public static async Task<RuntimeInfo> DiscoverAsync(bool preferDev, bool autoStart = true, TimeSpan? timeout = null)
    {
        var existing = await FindRunningEngineAsync(preferDev);
        if (existing is not null)
        {
            return existing;
        }

        // A debug engine is intentionally deterministic (fixed loopback port and token). It is
        // safe to probe only when the Desktop itself was launched with --dev, and prevents a
        // missing/stale development runtime.json from disconnecting local debug sessions.
        var development = await FindDevelopmentEngineAsync(preferDev);
        if (development is not null)
        {
            return development;
        }

        if (!autoStart)
        {
            throw new InvalidOperationException("Subconscious engine is not running and auto-start is disabled.");
        }

        try
        {
            SpawnEngine(preferDev);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Couldn't start the Subconscious engine because the 'subconscious' executable is not available on PATH.",
                exception);
        }

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            existing = await FindRunningEngineAsync(preferDev);
            if (existing is not null)
            {
                return existing;
            }

            // Keep checking the deterministic dev endpoint while a new process starts. This
            // also covers an interrupted debug shutdown that left runtime.json unavailable.
            development = await FindDevelopmentEngineAsync(preferDev);
            if (development is not null)
            {
                return development;
            }
        }

        throw new TimeoutException("Timed out waiting for the Subconscious engine to start.");
    }

    private static async Task<RuntimeInfo?> FindRunningEngineAsync(bool dev)
    {
        var info = ReadRuntimeInfo(DataDirectory(dev));
        return info is not null && await IsReachableAsync(info) ? info : null;
    }

    private static async Task<RuntimeInfo?> FindDevelopmentEngineAsync(bool preferDev)
    {
        if (!preferDev)
        {
            return null;
        }

        var info = new RuntimeInfo
        {
            Host = DevelopmentHost,
            Port = DevelopmentPort,
            Token = DevelopmentToken,
            Pid = 0,
            Version = "development",
        };
        return await IsReachableAsync(info) ? info : null;
    }

    private static RuntimeInfo? ReadRuntimeInfo(string dataDirectory)
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(dataDirectory, RuntimeFileName));
            return JsonSerializer.Deserialize<RuntimeInfo>(json);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> IsReachableAsync(RuntimeInfo info)
    {
        try
        {
            var response = await HealthClient.GetAsync($"http://{info.Host}:{info.Port}/api/v1/health");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static void SpawnEngine(bool dev)
    {
        var args = dev ? "engine --dev --headless" : "engine --headless";
        Process.Start(new ProcessStartInfo("subconscious", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
