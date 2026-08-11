using Microsoft.Maui.Hosting;
#if WINDOWS
using Subconscious.WYSIWYG.Platforms.Windows;
#endif

namespace Subconscious.WYSIWYG;

public static class MauiAppBuilderExtensions
{
    /// <summary>Registers the platform-native rich document editor.</summary>
    public static MauiAppBuilder UseSubconsciousWysiwyg(this MauiAppBuilder builder)
    {
#if WINDOWS
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<NativeDocumentEditor, NativeDocumentEditorHandler>());
#endif
        return builder;
    }
}
