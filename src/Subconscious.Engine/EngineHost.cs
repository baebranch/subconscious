using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Subconscious.Engine;

/// <summary>
/// Composition root for the engine. Mirrors the role of <c>Engine.start_engine</c> in
/// <c>engine.py</c>: builds the generic host, registers engine services, and exposes a
/// single entry point for CLI hosts (desktop/web/headless) to start the engine with.
///
/// This is an intentionally thin placeholder for Phase 0 scaffolding — service
/// registration (database, agent manager, tool registry, RAG indexer, local API, etc.)
/// lands incrementally starting Phase 1 per translation.md §7.
/// </summary>
public static class EngineHost
{
    public static IHostBuilder CreateHostBuilder(EngineConfig config) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(options =>
                {
                    // Mirrors the dev/prod log line formats configured in cli/__init__.py.
                    options.SingleLine = true;
                    options.TimestampFormat = config.Dev
                        ? "yyyy-MM-dd HH:mm:ss.fff "
                        : "yyyy-MM-dd HH:mm:ss ";
                });
                logging.SetMinimumLevel(config.Dev ? LogLevel.Debug : LogLevel.Information);
            })
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton(config);
                // Phase 1+: register EngineConfig-derived services (data dir bootstrap,
                // secrets store, DbContext, EventBus, JobManager, AgentManager, ...).
            });
}
