using System.Text.Json;
using System.Text.Json.Serialization;

namespace Subconscious.Engine.Api;

/// <summary>
/// Shape of the on-disk <c>runtime.json</c> discovery file, written to
/// <see cref="EngineConfig.DataDirectory"/> once the local API is actually listening.
/// Byte-for-byte compatible with the TypeScript client's <c>RuntimeInfo</c> interface
/// (<c>subconscious-code/src/engine/types.ts</c>) so any existing/future client can
/// discover this engine the same way it discovers the Python one.
/// </summary>
public sealed record RuntimeInfoFile
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Token { get; init; }
    public required int Pid { get; init; }
    public required string Version { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }
}

/// <summary>
/// Reads/writes the <c>runtime.json</c> discovery file. This is the sole discovery
/// mechanism (no HTTP-exposed "runtime.json" endpoint) — a client that wants to find a
/// running engine reads this file from the well-known data directory and probes
/// <c>/api/v1/health</c>, exactly like <c>subconscious-code/src/engine/discovery.ts</c>.
/// </summary>
public static class RuntimeInfoWriter
{
    public const string FileName = "runtime.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        // Byte-for-byte compatible with subconscious-code's RuntimeInfo interface
        // (host/port/token/pid/version/node_id, all lowercase) — PascalCase C# property
        // names must be projected to that exact casing, not left at their .NET default.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string PathFor(string dataDirectory) => Path.Combine(dataDirectory, FileName);

    /// <summary>
    /// Write the discovery file, creating the data directory if needed. The completed JSON is
    /// moved into place atomically so a concurrently starting Desktop never reads a truncated
    /// record and mistakes a live Engine for an unavailable one.
    /// </summary>
    public static void Write(string dataDirectory, RuntimeInfoFile info)
    {
        Directory.CreateDirectory(dataDirectory);

        var destination = PathFor(dataDirectory);
        var temporary = Path.Combine(
            dataDirectory,
            $".{FileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(info, SerializerOptions);

        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            // A failed write must not leave a future discovery attempt with a misleading file.
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // Best effort: the destination is still authoritative.
            }
        }
    }

    /// <summary>Read the discovery file, or <see langword="null"/> if absent/unreadable.</summary>
    public static RuntimeInfoFile? Read(string dataDirectory)
    {
        try
        {
            var json = File.ReadAllText(PathFor(dataDirectory));
            return JsonSerializer.Deserialize<RuntimeInfoFile>(json);
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

    /// <summary>Remove the discovery file on clean shutdown so a stale file isn't found later.</summary>
    public static void Delete(string dataDirectory)
    {
        try
        {
            File.Delete(PathFor(dataDirectory));
        }
        catch (IOException)
        {
            // Best-effort; a stale file is harmless since discovery also probes /health.
        }
    }
}
