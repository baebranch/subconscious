using Microsoft.AspNetCore.Http;

namespace Subconscious.Engine.Api;

/// <summary>
/// Enforces the bearer-token contract described in translation.md/discovery.ts: every request
/// under <c>/api/v1</c> except <c>/api/v1/health</c> must present the token from
/// <c>runtime.json</c> through an <c>Authorization: Bearer</c> header. The legacy
/// <c>?token=</c> query parameter remains accepted for existing WebSocket clients, but new
/// clients use the header so credentials are not embedded in URLs.
/// </summary>
public sealed class BearerTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly EngineAuthToken _token;

    public BearerTokenMiddleware(RequestDelegate next, EngineAuthToken token)
    {
        _next = next;
        _token = token;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api/v1") || path.StartsWithSegments("/api/v1/health"))
        {
            await _next(context);
            return;
        }

        var presented = ExtractToken(context);
        if (!string.Equals(presented, _token.Value, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid bearer token." });
            return;
        }

        await _next(context);
    }

    private static string? ExtractToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        if (context.Request.Query.TryGetValue("token", out var queryToken))
        {
            return queryToken.ToString();
        }

        return null;
    }
}

public static class BearerTokenMiddlewareExtensions
{
    public static IApplicationBuilder UseEngineBearerAuth(this IApplicationBuilder app)
    {
        return app.UseMiddleware<BearerTokenMiddleware>();
    }
}
