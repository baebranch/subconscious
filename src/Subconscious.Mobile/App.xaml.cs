using Microsoft.Extensions.DependencyInjection;

namespace Subconscious.Mobile;

public partial class App : Application
{
	public App(MobileAppearancePreferences appearancePreferences)
	{
		InitializeComponent();
		appearancePreferences.Initialize(this);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}