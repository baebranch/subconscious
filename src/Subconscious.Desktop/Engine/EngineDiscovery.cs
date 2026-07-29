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
    /// Find a reachable engine, starting one if none is running and
    /// <paramref name="autoStart"/> is true. Probes the dev data dir first, then the
    /// non-dev one (or vice versa), matching the TS client's dual-candidate search so a
    /// dev daemon is still found by a non-dev client and vice versa.
    /// </summary>
    public static async Task<RuntimeInfo> DiscoverAsync(bool preferDev, bool autoStart = true, TimeSpan? timeout = null)
    {
        var existing = await FindRunningEngineAsync(preferDev);
        if (existing is not null)
        {
            return existing;
        }

        if (!autoStart)
        {
            throw new InvalidOperationException("Subconscious engine is not running and auto-start is disabled.");
        }

        SpawnEngine(preferDev);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            var info = await FindRunningEngineAsync(preferDev);
            if (info is not null)
            {
                return info;
            }
        }

        throw new TimeoutException("Timed out waiting for the Subconscious engine to start.");
    }

    private static async Task<RuntimeInfo?> FindRunningEngineAsync(bool preferDev)
    {
        var candidates = new[] { DataDirectory(preferDev), DataDirectory(!preferDev) };
        foreach (var dir in candidates)
        {
            var info = ReadRuntimeInfo(dir);
            if (info is null)
            {
                continue;
            }
            if (await IsReachableAsync(info))
            {
                return info;
            }
        }
        return null;
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
        var args = dev ? "--dev engine" : "engine";
        try
        {
            Process.Start(new ProcessStartInfo("subconscious", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // "subconscious" isn't on PATH — nothing more this client can do; the caller's
            // DiscoverAsync will time out and surface a clear error instead.
        }
    }
}
