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

    private void OnNavigateClicked(object? sender, EventArgs e)
    {
        if (sender is ImageButton { CommandParameter: MobileContextSection section })
        {
            _session.SelectContext(section);
        }
    }

    private async void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (sender is Picker { SelectedItem: Workspace workspace }
            && _session.CurrentWorkspace?.Uuid != workspace.Uuid)
        {
            await _session.SelectWorkspaceAsync(workspace);
        }
    }

    private async void OnThreadSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ThreadInfo thread) return;
        await _session.SelectThreadAsync(thread);
        _session.OpenChat();
        if (sender is CollectionView list) list.SelectedItem = null;
        FlyoutIsPresented = false;
    }

    private void OnWorkspaceSettingsSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Workspace workspace) return;
        _session.OpenWorkspaceSettings(workspace);
        if (sender is CollectionView list) list.SelectedItem = null;
        FlyoutIsPresented = false;
    }

    private void OnOpenChatClicked(object? sender, EventArgs e)
    {
        _session.OpenChat();
        FlyoutIsPresented = false;
    }

    private void OnOpenFilesClicked(object? sender, EventArgs e)
    {
        _session.OpenFiles();
        FlyoutIsPresented = false;
    }

    private void OnSettingsSelected(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: MobileSettingsPage page })
        {
            _session.OpenSettings(page);
            FlyoutIsPresented = false;
        }
    }

    private void OnOpenAccountClicked(object? sender, EventArgs e)
    {
        _session.OpenAccount();
        FlyoutIsPresented = false;
    }
}
