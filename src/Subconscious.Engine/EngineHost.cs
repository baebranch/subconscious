using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Subconscious.Engine.Api;
using Subconscious.Engine.Data;

namespace Subconscious.Engine;

/// <summary>
/// Composition root for the engine. Mirrors the role of <c>Engine.start_engine</c> in
/// <c>engine.py</c>: builds the host, registers every engine service, binds Kestrel to a
/// loopback-only port (when <see cref="EngineConfig.Api"/> is set), and writes/removes the
/// <c>runtime.json</c> discovery file around the host's lifetime so clients can find this
/// engine exactly like they find the Python one.
/// </summary>
public static class EngineHost
{
    // Stable loopback endpoint for the local development workflow. These values are deliberately
    // used only for --dev; regular engine launches retain an OS-assigned port and fresh token.
    private const int DevelopmentPort = 55681;
    private const string DevelopmentToken = "subconscious-dev-token";

    /// <summary>
    /// Build the engine host. Returns a <see cref="WebApplication"/> (which implements
    /// <see cref="IHost"/>) so callers that only need generic host behavior — e.g. the
    /// tray coordinator's <c>host.StopAsync()</c> — don't need to know it's ASP.NET Core
    /// underneath.
    /// </summary>
    public static WebApplication Build(EngineConfig config)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            // Mirrors the dev/prod log line formats configured in cli/__init__.py.
            options.SingleLine = true;
            options.TimestampFormat = config.Dev
                ? "yyyy-MM-dd HH:mm:ss.fff "
                : "yyyy-MM-dd HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(config.Dev ? LogLevel.Debug : LogLevel.Information);

        // Loopback remains the safe default. LAN access is an explicit, authenticated opt-in;
        // its port is still dynamic unless the caller requested one, so paired clients must use
        // the invitation printed by the CLI rather than assume a fixed production port.
        var listenPort = config.Dev ? DevelopmentPort : config.Port;
        builder.WebHost.UseKestrel(kestrel =>
        {
            if (config.LanEnabled)
            {
                kestrel.ListenAnyIP(listenPort);
            }
            else
            {
                kestrel.Listen(System.Net.IPAddress.Loopback, listenPort);
            }
        });
        // Suppress the ASP.NET Core startup banner lines ("Now listening on...",
        // "Application started...") that would otherwise print regardless of the
        // configured log level — this is a background engine, not a web app being
        // manually driven from a console.
        builder.WebHost.UseSetting("suppressStatusMessages", "true");

        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Dev
            ? new EngineAuthToken(DevelopmentToken)
            : EngineAuthToken.Generate());

        Directory.CreateDirectory(config.DataDirectory);
        var dbPath = Path.Combine(config.DataDirectory, "subconscious.db");
        builder.Services.AddDbContext<SubconsciousDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        builder.Services.AddEngineServices();

        var app = builder.Build();

        app.UseEngineBearerAuth();
        app.UseWebSockets();
        app.MapEngineEndpoints();

        return app;
    }

    /// <summary>
    /// Start Kestrel, ensure the database exists, and write <c>runtime.json</c> with the
    /// actually-bound port. Call <see cref="StopEngineAsync"/> (or dispose the app) to
    /// clean up the discovery file on shutdown.
    /// </summary>
    public static async Task StartEngineAsync(WebApplication app, EngineConfig config)
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SubconsciousDbContext>();

            // Creates the database if it's missing entirely, applies any pending migrations if
            // it isn't, and transparently baselines pre-migrations databases (from earlier .NET
            // builds using EnsureCreated, or from the original Python engine) onto the migration
            // history in place — see DatabaseMigrator for the reasoning.
            await DatabaseMigrator.MigrateAsync(
                db,
                scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator)));
        }

        await app.StartAsync();

        var boundPort = GetBoundPort(app);
        var token = app.Services.GetRequiredService<EngineAuthToken>();
        RuntimeInfoWriter.Write(config.DataDirectory, new RuntimeInfoFile
        {
            Host = "127.0.0.1",
            Port = boundPort,
            Token = token.Value,
            Pid = Environment.ProcessId,
            Version = Constants.Version,
            NodeId = config.NodeId,
        });
    }

    /// <summary>Remove the discovery file; safe to call even if the engine never fully started.</summary>
    public static void StopEngine(EngineConfig config) => RuntimeInfoWriter.Delete(config.DataDirectory);

    /// <summary>
    /// Produces copyable, short-lived mobile invitations for an explicitly LAN-enabled engine.
    /// The bearer token is intentionally never advertised over the network; the local console is
    /// a user-mediated transfer channel and the token expires when this engine process exits.
    /// </summary>
    public static IReadOnlyList<string> GetLanPairingInvitations(WebApplication app, EngineConfig config)
    {
        if (!config.LanEnabled)
        {
            return [];
        }

        var port = GetBoundPort(app);
        var token = app.Services.GetRequiredService<EngineAuthToken>().Value;
        var name = Uri.EscapeDataString(Environment.MachineName);
        var hosts = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !System.Net.IPAddress.IsLoopback(address) && IsPrivateIpv4(address))
            .Select(address => address.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return hosts.Select(host =>
            $"subconscious://pair?host={host}&port={port}&token={Uri.EscapeDataString(token)}&name={name}").ToArray();
    }

    private static bool IsPrivateIpv4(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }

    private static int GetBoundPort(WebApplication app)
    {
        var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addressFeature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var address = addressFeature?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report a bound address.");
        return new Uri(address).Port;
    }
}
