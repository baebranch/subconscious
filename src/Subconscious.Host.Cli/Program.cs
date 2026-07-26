using System.CommandLine;
using Microsoft.Extensions.Hosting;
using Subconscious.Engine;

namespace Subconscious.Host.Cli;

/// <summary>
/// CLI entry point. Mirrors <c>cli/__init__.py</c>'s subcommand shape:
/// <c>engine</c>, <c>desktop</c> (default), <c>web</c>, and the dev-only <c>code</c>
/// (TUI) subcommand, plus the shared <c>--dev</c> / <c>--no-api</c> flags.
///
/// Adds one flag with no Python-side equivalent: <c>--headless</c>. It skips the desktop
/// GUI window only (a no-op today — the desktop client doesn't exist yet, Phase 6). The
/// system tray icon is shown regardless, on platforms that support one (see
/// <see cref="Subconscious.Desktop.Tray.TrayIconServiceFactory"/>), so the engine always
/// has a visible "still running, here's how to reach it" presence — closer to how the
/// Python desktop app behaves once minimized to tray (<c>desktop/tray.py</c>) than to the
/// Python <c>engine</c> subcommand, which had no UI presence of any kind.
///
/// Only the <c>engine</c> subcommand is wired to real behavior in this Phase 1 scaffold;
/// <c>desktop</c>/<c>web</c>/<c>code</c> are stubbed pending their respective phases
/// (translation.md §7).
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
        var headlessOption = new Option<bool>("--headless")
        {
            Description = "Run without the desktop GUI window. The system tray icon " +
                           "(on platforms that support one) is still shown."
        };

        var rootCommand = new RootCommand("Subconscious: A Distributed Agentic Engine")
        {
            devOption,
            noApiOption,
            headlessOption
        };

        var engineCommand = new Command(
            "engine",
            "Starts only the engine, with a system tray icon on platforms that support one.")
        {
            devOption,
            noApiOption,
            headlessOption
        };
        engineCommand.SetAction(async parseResult =>
        {
            var config = new EngineConfig(
                Dev: parseResult.GetValue(devOption),
                Api: !parseResult.GetValue(noApiOption),
                Gui: false,
                Tui: false,
                Headless: parseResult.GetValue(headlessOption));
            await RunEngineAsync(config);
        });

        var desktopCommand = new Command("desktop", "Starts the engine with the desktop interface (default).")
        {
            devOption,
            noApiOption,
            headlessOption
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
        rootCommand.SetAction(_ =>
        {
            Console.WriteLine("Desktop client is not yet implemented (see translation.md, Phase 6).");
            return Task.CompletedTask;
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

        using var tray = EngineTrayCoordinator.AttachIfSupported(host, config);
        if (tray is not null)
        {
            Console.WriteLine("Tray icon ready. Right-click it to open or exit Subconscious.");
        }
        else
        {
            Console.WriteLine("No tray icon backend available on this platform yet; running without one.");
        }

        Console.WriteLine("Engine started. Press Ctrl+C to exit.");
        await host.RunAsync();
    }
}
