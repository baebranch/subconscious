using Subconscious.Desktop.Engine;

namespace Subconscious.Terminal;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(IsHelp))
        {
            Console.WriteLine("Subconscious Terminal\n\nUsage: subconscious-terminal [--dev] [--plain]");
            return 0;
        }

        var dev = args.Any(argument => argument.Equals("--dev", StringComparison.OrdinalIgnoreCase));
        var plain = args.Any(argument => argument.Equals("--plain", StringComparison.OrdinalIgnoreCase));

        try
        {
            using var terminal = TerminalSession.Open(plain);
            await using var client = new EngineClient();
            var application = new TerminalApp(client, terminal, dev);
            return await application.RunAsync();
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
}
