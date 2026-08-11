using MauiIcons.Fluent;

namespace Subconscious.Chat.Debug;

public enum RendererKind
{
    Native,
    Web,
}

public static class MauiProgram
{
    public static RendererKind SelectedRenderer { get; private set; } = RendererKind.Native;

    public static MauiApp CreateMauiApp()
    {
        SelectedRenderer = ParseRenderer(Environment.GetCommandLineArgs());
        return MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseFluentMauiIcons()
            .Build();
    }

    private static RendererKind ParseRenderer(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--renderer", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Count)
            {
                return string.Equals(args[index + 1], "web", StringComparison.OrdinalIgnoreCase)
                    ? RendererKind.Web
                    : RendererKind.Native;
            }
        }

        return RendererKind.Native;
    }
}
