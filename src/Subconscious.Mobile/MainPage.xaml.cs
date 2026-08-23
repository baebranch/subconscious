using Subconscious.Mobile.Engine;

namespace Subconscious.Mobile;

[QueryProperty(nameof(Section), "section")]
public partial class MainPage : ContentPage
{
    private readonly MobileChatSession _session;
    private string _section = "chat";

    public MainPage()
    {
        InitializeComponent();
        _session = IPlatformApplication.Current?.Services.GetService<MobileChatSession>()
            ?? throw new InvalidOperationException("MobileChatSession is not registered.");
        BindingContext = _session;
        ApplySection();
    }

    public string Section
    {
        get => _section;
        set
        {
            _section = string.IsNullOrWhiteSpace(value) ? "chat" : Uri.UnescapeDataString(value).ToLowerInvariant();
            ApplySection();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _session.InitializeAsync();
    }

    private void ApplySection()
    {
        if (ChatScreen is null) return;
        ChatScreen.IsVisible = _section is "chat" or "home";
        WorkspacesScreen.IsVisible = _section == "workspaces";
        ThreadsScreen.IsVisible = _section == "threads";
        FilesScreen.IsVisible = _section == "files";
        SettingsScreen.IsVisible = _section == "settings";
        AccountScreen.IsVisible = _section == "account";
        Title = _section switch { "workspaces" => "Workspaces", "threads" => "Threads", "files" => "Files", "settings" => "Settings", "account" => "Account", _ => "Chat" };
    }

    private async void OnWorkspaceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Workspace workspace)
        {
            await _session.SelectWorkspaceAsync(workspace);
            await Shell.Current.GoToAsync("//Home?section=chat");
        }
    }

    private async void OnThreadSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ThreadInfo thread)
        {
            await _session.SelectThreadAsync(thread);
            await Shell.Current.GoToAsync("//Home?section=chat");
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e) => await _session.SendAsync();
    private void OnStopClicked(object? sender, EventArgs e) => _session.Stop();
    private void OnNewThreadClicked(object? sender, EventArgs e) => _session.StartNewThread();
}
