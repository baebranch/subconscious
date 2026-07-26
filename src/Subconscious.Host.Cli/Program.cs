using System.CommandLine;
using Microsoft.Extensions.Hosting;
using Subconscious.Engine;

namespace Subconscious.Host.Cli;

/// <summary>
/// CLI entry point. Mirrors <c>cli/__init__.py</c>'s subcommand shape:
/// <c>engine</c> (headless), <c>desktop</c> (default), <c>web</c>, and the dev-only
/// <c>code</c> (TUI) subcommand, plus the shared <c>--dev</c> / <c>--no-api</c> flags.
///
/// Only the <c>engine</c> subcommand is wired to real behavior in this Phase 0
/// scaffold; <c>desktop</c>/<c>web</c>/<c>code</c> are stubbed pending their
/// respective phases (translation.md §7).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var devOption = new Option<bool>("--dev")
        {
            Description = "Run in development mode (isolated data directory, verbose logging)."
        };
        var noApiOption = new Option<bool>("--no-api")
        {
            Description = "Run the engine without starting the local loopback API."
        };

        var rootCommand = new RootCommand("Subconscious: A Distributed Agentic Engine")
        {
            devOption,
            noApiOption
        };

        var engineCommand = new Command("engine", "Starts only the engine (headless).")
        {
            devOption,
            noApiOption
        };
        engineCommand.SetAction(async parseResult =>
        {
            var config = new EngineConfig(
                Dev: parseResult.GetValue(devOption),
                Api: !parseResult.GetValue(noApiOption),
                Gui: false,
                Tui: false);
            await RunEngineAsync(config);
        });

        var desktopCommand = new Command("desktop", "Starts the engine with the desktop interface (default).")
        {
            devOption,
            noApiOption
        };
        desktopCommand.SetAction(_ =>
        {
            Console.WriteLine("Desktop client is not yet implemented (see translation.md, Phase 6).");
            return Task.FromResult(1);
        });

        var webCommand = new Command("web", "Starts the engine with the web interface.")
        {
            devOption,
            noApiOption
        };
        webCommand.SetAction(_ =>
        {
            Console.WriteLine("Web client is not yet implemented (see translation.md, Phase 8).");
            return Task.FromResult(1);
        });

        var codeCommand = new Command("code", "Starts the engine with the Terminal TUI interface.")
        {
            devOption,
            noApiOption
        };
        codeCommand.SetAction(_ =>
        {
            Console.WriteLine("TUI client is not yet implemented (see translation.md, Phase 7).");
            return Task.FromResult(1);
        });

        rootCommand.Subcommands.Add(engineCommand);
        rootCommand.Subcommands.Add(desktopCommand);
        rootCommand.Subcommands.Add(webCommand);
        rootCommand.Subcommands.Add(codeCommand);

        // No subcommand -> defaults to desktop, mirroring the Python CLI's fallback.
        rootCommand.SetAction(async parseResult =>
        {
            var config = new EngineConfig(
                Dev: parseResult.GetValue(devOption),
                Api: !parseResult.GetValue(noApiOption),
                Gui: true,
                Tui: false);
            Console.WriteLine("Desktop client is not yet implemented (see translation.md, Phase 6).");
            await Task.CompletedTask;
        });

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static async Task RunEngineAsync(EngineConfig config)
    {
        Console.WriteLine(Logo.Text);
        using var host = EngineHost.CreateHostBuilder(config).Build();
        Console.WriteLine($"Subconscious Engine {Constants.Version}");
        Console.WriteLine($"Data directory: {config.DataDirectory}");
        Console.WriteLine("Engine scaffold started (Phase 0). Press Ctrl+C to exit.");
        await host.RunAsync();
    }
}
