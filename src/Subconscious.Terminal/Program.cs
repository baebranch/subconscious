using Subconscious.Desktop.Engine;

namespace Subconscious.Terminal;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(IsHelp))
        {
            Console.WriteLine("Subconscious Terminal\n\nUsage: subconscious-terminal [--dev] [--ansi|--plain]\n\nDefault: full-screen Subconscious.TUI renderer with transcript, composer, status, and navigation sidebar.");
            return 0;
        }

        var dev = args.Any(argument => argument.Equals("--dev", StringComparison.OrdinalIgnoreCase));
        var gui = args.Any(argument => argument.Equals("--gui", StringComparison.OrdinalIgnoreCase));
        var ansi = args.Any(argument => argument.Equals("--ansi", StringComparison.OrdinalIgnoreCase));
        var plain = args.Any(argument => argument.Equals("--plain", StringComparison.OrdinalIgnoreCase));
        if (gui)
        {
            Console.Error.WriteLine("The Terminal.Gui renderer has been removed. Use the default Subconscious.TUI renderer.");
            return 2;
        }
        if (ansi && plain)
        {
            Console.Error.WriteLine("Choose only one renderer: --ansi or --plain.");
            return 2;
        }

        var mode = plain ? RendererMode.Plain : RendererMode.Ansi;

        try
        {
            await using var client = new EngineClient();
            using var terminal = TerminalSession.Open(mode == RendererMode.Plain);
            return await new TerminalApp(client, terminal, dev).RunAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(dev
                ? $"Unable to start Subconscious Terminal:\n{exception}"
                : $"Unable to start Subconscious Terminal: {exception.Message}");
            return 1;
        }
    }

    private static bool IsHelp(string argument) =>
        argument.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || argument.Equals("-h", StringComparison.OrdinalIgnoreCase)
        || argument.Equals("/?", StringComparison.OrdinalIgnoreCase);

    private enum RendererMode
    {
        Ansi,
        Plain,
    }
}
