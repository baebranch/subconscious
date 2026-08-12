using Subconscious.WYSIWYG;

namespace Subconscious.WYSIWYG.Debug;

public sealed class App : Application
{
    private readonly MainPage _mainPage = new();
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
        var window = new Window(_mainPage) { Title = "WYSIWYG Debug — native-only" };
#if WINDOWS
        window.Width = 1120;
        window.Height = 780;
        window.MinimumWidth = 720;
        window.MinimumHeight = 520;
#endif
        return window;
    }

    private void ApplyTheme()
    {
        UserAppTheme = _dark ? AppTheme.Dark : AppTheme.Light;
        _mainPage.SetTheme(_dark ? EditorTheme.Dark : EditorTheme.Light);
    }
}
