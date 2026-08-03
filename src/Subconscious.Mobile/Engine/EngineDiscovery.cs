using System.Net.Http;
using System.Text.Json;

namespace Subconscious.Mobile.Engine;

/// <summary>
/// Locates a running Subconscious engine reachable from this device. Structurally mirrors
/// <c>Subconscious.Desktop.Engine.EngineDiscovery</c> (read <c>runtime.json</c> from the
/// well-known data directory, probe <c>/api/v1/health</c>), which is meaningful on the
/// Windows/MacCatalyst mobile targets that share a filesystem with a desktop-hosted engine.
/// On sandboxed Android/iOS there is no local engine process to discover this way — those
/// targets have no access to another app's <c>runtime.json</c> — so discovery there will
/// simply find nothing and the caller should fall back to a configured host/port (not yet
/// implemented; tracked as a follow-up once remote/paired-engine access is designed).
/// </summary>
public static class EngineDiscovery
{
    private const string RuntimeFileName = "runtime.json";
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>The data directory the engine reads/writes for the given dev mode, mirroring
    /// <c>EngineConfig.DataDirectory</c> and the desktop client's copy of the same logic.</summary>
    public static string DataDirectory(bool dev)
    {
        string baseDir;
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetEnvironmentVariable("APPDATA")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            baseDir = Path.Combine(appData, "Subconscious");
        }
        else if (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support", "Subconscious");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, ".config", "subconscious");
        }
        return dev ? baseDir + "-dev" : baseDir;
    }

    /// <summary>
    /// Find a reachable engine. Probes the dev data dir first, then the non-dev one (or vice
    /// versa), matching the desktop client's dual-candidate search. Does not auto-start an
    /// engine process — unlike the desktop client, a mobile client has no business spawning a
    /// process on this device (there usually isn't one to spawn).
    /// </summary>
    public static async Task<RuntimeInfo> DiscoverAsync(bool preferDev, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        do
        {
            var existing = await FindRunningEngineAsync(preferDev);
            if (existing is not null)
            {
                return existing;
            }
            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);

        throw new InvalidOperationException(
            "No Subconscious engine found. On Android/iOS this client cannot discover an " +
            "engine running on another device yet — this only works today on Windows/MacCatalyst " +
            "builds running alongside the engine.");
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
        catch (UnauthorizedAccessException)
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
}
