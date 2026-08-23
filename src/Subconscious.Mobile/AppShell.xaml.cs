using Subconscious.Mobile.Engine;

namespace Subconscious.Mobile;

public partial class AppShell : Shell
{
    private readonly MobileChatSession _session;

    public AppShell()
    {
        InitializeComponent();
        _session = IPlatformApplication.Current?.Services.GetService<MobileChatSession>()
            ?? throw new InvalidOperationException("MobileChatSession is not registered.");
        BindingContext = _session;
    }

    private async void OnNavigateTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TapGestureRecognizer { CommandParameter: string section }) return;
        FlyoutIsPresented = false;
        await GoToAsync($"//Home?section={Uri.EscapeDataString(section)}");
    }

    private async void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (sender is Picker { SelectedItem: Workspace workspace })
        {
            await _session.SelectWorkspaceAsync(workspace);
        }
    }

    private async void OnThreadSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ThreadInfo thread) return;
        await _session.SelectThreadAsync(thread);
        FlyoutIsPresented = false;
        await GoToAsync("//Home?section=chat");
    }

    private async void OnNewConversationClicked(object? sender, EventArgs e)
    {
        _session.StartNewThread();
        FlyoutIsPresented = false;
        await GoToAsync("//Home?section=chat");
    }
}
