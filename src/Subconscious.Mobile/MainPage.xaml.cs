using Subconscious.Mobile.Engine;

namespace Subconscious.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileChatSession _session;

    public MainPage()
    {
        InitializeComponent();
        _session = IPlatformApplication.Current?.Services.GetService<MobileChatSession>()
            ?? throw new InvalidOperationException("MobileChatSession is not registered.");
        BindingContext = _session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _session.InitializeAsync();
    }

    private async void OnSendClicked(object? sender, EventArgs e) => await _session.SendAsync();
    private void OnStopClicked(object? sender, EventArgs e) => _session.Stop();
    private void OnNewThreadClicked(object? sender, EventArgs e) => _session.StartNewThread();
    private async void OnSaveWorkspaceClicked(object? sender, EventArgs e) =>
        await _session.SaveWorkspaceSettingsAsync();
    private async void OnPairEngineClicked(object? sender, EventArgs e) =>
        await _session.PairEngineAsync();
    private async void OnForgetEngineClicked(object? sender, EventArgs e) =>
        await _session.ForgetPairedEngineAsync();
}
