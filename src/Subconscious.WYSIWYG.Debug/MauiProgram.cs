using Subconscious.WYSIWYG;

namespace Subconscious.WYSIWYG.Debug;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
        .UseMauiApp<App>()
        .UseSubconsciousWysiwyg()
        .Build();
}
