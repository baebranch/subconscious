using System.Text.Json;

namespace Subconscious.Mobile.Engine;

/// <summary>A LAN endpoint deliberately paired by the user; its bearer credential is device-local.</summary>
public sealed record EngineEndpoint(string Host, int Port, string Token, string? Name = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Host}:{Port}" : Name;

    public RuntimeInfo ToRuntimeInfo() => new()
    {
        Host = Host,
        Port = Port,
        Token = Token,
        Pid = 0,
        Version = "paired",
    };
}

/// <summary>Stores a single mobile pairing in platform-protected secure storage.</summary>
public sealed class PairedEngineStore
{
    private const string StorageKey = "subconscious.paired-engine.v1";

    public async Task<EngineEndpoint?> LoadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(StorageKey);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<EngineEndpoint>(json);
        }
        catch (Exception)
        {
            // Secure storage can be unavailable during early emulator/device initialization.
            // Treat that as no pairing; callers can still use their local development endpoint.
            return null;
        }
    }

    public Task SaveAsync(EngineEndpoint endpoint) =>
        SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(endpoint));

    public void Remove() => SecureStorage.Default.Remove(StorageKey);
}

/// <summary>Validates the user-mediated pairing invitation printed by an opt-in LAN engine.</summary>
public static class EnginePairingInvitation
{
    public static EngineEndpoint Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "subconscious", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "pair", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Paste a valid Subconscious pairing invitation.");
        }

        var values = ParseQuery(uri.Query);
        if (!values.TryGetValue("host", out var host) || string.IsNullOrWhiteSpace(host) ||
            !values.TryGetValue("port", out var portText) || !int.TryParse(portText, out var port) || port is < 1 or > 65535 ||
            !values.TryGetValue("token", out var token) || string.IsNullOrWhiteSpace(token))
        {
            throw new FormatException("The pairing invitation is missing a valid host, port, or credential.");
        }

        return new EngineEndpoint(host, port, token, values.GetValueOrDefault("name"));
    }

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Where(pair => pair.Length == 2)
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.OrdinalIgnoreCase);
}
