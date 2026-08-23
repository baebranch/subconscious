using Microsoft.Extensions.Logging;
using MauiIcons.Fluent;
using Subconscious.Mobile.Engine;
using Subconscious.WYSIWYG;

namespace Subconscious.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseFluentMauiIcons()
			.UseSubconsciousWysiwyg()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Singletons: one engine connection and one shared workspace list for the whole app.
		// WorkspacesPage is resolved by Shell via a parameterless-constructor DataTemplate
		// (Shell.FlyoutContent's ShellContent doesn't support constructor DI), so it reaches
		// these through IPlatformApplication.Current.Services rather than a ctor parameter.
		builder.Services.AddSingleton<EngineClient>();
		builder.Services.AddSingleton<WorkspaceStore>();
		builder.Services.AddSingleton<MobileChatSession>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
