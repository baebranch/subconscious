using System.Security.Cryptography;

namespace Subconscious.Engine.Api;

/// <summary>
/// The bearer token clients must present to use the local API, generated fresh on every
/// engine start and shared with clients only via the <c>runtime.json</c> discovery file
/// (never logged, never written anywhere else). Mirrors the Python engine's per-run
/// token in <c>api/runtime.py</c>.
/// </summary>
public sealed record EngineAuthToken(string Value)
{
    public static EngineAuthToken Generate()
    {
        // 32 random bytes, base64url-encoded: long enough to make guessing infeasible for a
        // loopback-only local service, short enough to fit comfortably in a WS query string.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return new EngineAuthToken(token);
    }
}
