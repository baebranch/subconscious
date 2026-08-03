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

        // Loopback-only binding. Development has a stable endpoint for debuggers and REST
        // clients; regular runs keep an explicitly requested port or the OS-assigned port 0.
        var listenPort = config.Dev ? DevelopmentPort : config.Port;
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(System.Net.IPAddress.Loopback, listenPort);
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

    private static int GetBoundPort(WebApplication app)
    {
        var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addressFeature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var address = addressFeature?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report a bound address.");
        return new Uri(address).Port;
    }
}
