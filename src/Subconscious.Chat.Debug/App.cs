namespace Subconscious.Chat.Debug;

public sealed class App : Application
{
    private bool _dark;

    public App()
    {
        ApplyTheme();
    }

    public void ToggleTheme()
    {
        _dark = !_dark;
        ApplyTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage(MauiProgram.SelectedRenderer, new SampleViewModel()))
        {
            Title = $"Chat Debug — {MauiProgram.SelectedRenderer}",
        };
#if WINDOWS
        window.Width = 1000;
        window.Height = 760;
        window.MinimumWidth = 640;
        window.MinimumHeight = 480;
#endif
        return window;
    }

    private void ApplyTheme()
    {
        UserAppTheme = _dark ? AppTheme.Dark : AppTheme.Light;
        ThemeResources.Replace(Resources, _dark);
    }
}
