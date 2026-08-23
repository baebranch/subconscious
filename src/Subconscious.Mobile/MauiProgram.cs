using Microsoft.Extensions.Logging;
using MauiIcons.Fluent;
using Subconscious.Mobile.Controls;
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
#if ANDROID
			// The omnibox Border owns its field outline. Remove Android's default underline only
			// from the scoped chat controls so normal form Editors and Pickers keep native chrome.
			.ConfigureMauiHandlers(_ =>
			{
				Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping(
					nameof(ChatComposerEditor), (handler, view) =>
					{
						if (view is ChatComposerEditor)
						{
							handler.PlatformView.BackgroundTintList =
								Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
							handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
						}
					});
				Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(
					nameof(ChatModelPicker), (handler, view) =>
					{
						if (view is ChatModelPicker)
						{
							handler.PlatformView.BackgroundTintList =
								Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
							handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
						}
					});
			})
#endif
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
		builder.Services.AddSingleton<PairedEngineStore>();
		builder.Services.AddSingleton<WorkspaceStore>();
		builder.Services.AddSingleton<MobileAppearancePreferences>();
		builder.Services.AddSingleton<MobileChatSession>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
