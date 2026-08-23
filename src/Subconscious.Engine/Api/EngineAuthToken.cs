using System.Security.Cryptography;

namespace Subconscious.Engine.Api;

/// <summary>
/// The bearer token clients must present to use the API. It is generated fresh on every engine
/// start and shared locally through <c>runtime.json</c>. When the user explicitly starts an
/// opt-in <c>--lan</c> engine, the CLI also shows a copyable, process-lifetime pairing invitation
/// on that same local console; it is never advertised on the network.
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
