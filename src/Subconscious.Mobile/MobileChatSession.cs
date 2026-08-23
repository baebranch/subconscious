using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Subconscious.Chat;
using Subconscious.Mobile.Engine;

namespace Subconscious.Mobile;

/// <summary>Shared phone chat state. It keeps Shell's flyout and the active page in sync.</summary>
public sealed partial class MobileChatSession : ObservableObject
{
    private readonly EngineClient _client;
    private readonly WorkspaceStore _workspaceStore;
    private bool _initialized;
    private string? _activeTurnId;
    private ChatMessage? _streamingMessage;

#if SUBCONSCIOUS_LOCAL_ENGINE
    private const bool UseDevelopmentEngine = true;
#else
    private const bool UseDevelopmentEngine = false;
#endif

    public MobileChatSession(EngineClient client, WorkspaceStore workspaceStore)
    {
        _client = client;
        _workspaceStore = workspaceStore;
    }

    public ObservableCollection<IChatTranscriptMessage> Messages { get; } = [];
    public ObservableCollection<Workspace> Workspaces => _workspaceStore.Workspaces;
    public ObservableCollection<ThreadInfo> Threads { get; } = [];
    public ObservableCollection<ModelInfo> AvailableModels { get; } = [];

    [ObservableProperty] private Workspace? _currentWorkspace;
    [ObservableProperty] private ThreadInfo? _currentThread;
    [ObservableProperty] private ModelInfo? _selectedModel;
    [ObservableProperty] private string _composerText = string.Empty;
    [ObservableProperty] private string _statusText = "Connecting…";
    [ObservableProperty] private bool _isBusy;

    public string HeaderTitle => CurrentThread?.Title ?? CurrentWorkspace?.Name ?? "Subconscious";

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        _client.ConnectionStatusChanged += (_, connected) => MainThread.BeginInvokeOnMainThread(() => StatusText = connected ? "Connected" : "Disconnected");
        _client.ChatDelta += (_, e) => MainThread.BeginInvokeOnMainThread(() => AppendDelta(e));
        _client.ChatDone += (_, e) => MainThread.BeginInvokeOnMainThread(() => _ = CompleteTurnAsync(e));
        _client.ChatCancelled += (_, e) => MainThread.BeginInvokeOnMainThread(() => FinishTurn("Generation stopped."));
        _client.ChatError += (_, e) => MainThread.BeginInvokeOnMainThread(() => FinishTurn(e.Error));
        _client.ToolApprovalRequested += (_, e) => MainThread.BeginInvokeOnMainThread(() => StatusText = $"Approval required for {e.ToolName}. Open this conversation on Desktop to decide.");

        try
        {
            await _client.ConnectAsync(dev: UseDevelopmentEngine);
            await _workspaceStore.RefreshAsync();
            AvailableModels.Clear();
            foreach (var model in await _client.ListModelsAsync()) AvailableModels.Add(model);
            SelectedModel = AvailableModels.FirstOrDefault();
            if (Workspaces.FirstOrDefault() is { } workspace) await SelectWorkspaceAsync(workspace);
            else StatusText = "Connected — create a workspace on Desktop to begin.";
        }
        catch (Exception ex)
        {
            StatusText = $"Can't reach the engine: {ex.Message}";
        }
    }

    public async Task RefreshAsync()
    {
        await _workspaceStore.RefreshAsync();
        if (CurrentWorkspace is null && Workspaces.FirstOrDefault() is { } workspace) await SelectWorkspaceAsync(workspace);
    }

    public async Task SelectWorkspaceAsync(Workspace workspace)
    {
        CurrentWorkspace = workspace;
        CurrentThread = null;
        Messages.Clear();
        Threads.Clear();
        try
        {
            foreach (var thread in (await _client.ListThreadsAsync(workspace.Uuid)).OrderByDescending(thread => thread.UpdatedAt)) Threads.Add(thread);
            if (Threads.FirstOrDefault() is { } initialThread) await SelectThreadAsync(initialThread);
            else StatusText = "New conversation";
        }
        catch (Exception ex) { StatusText = $"Couldn't load threads: {ex.Message}"; }
    }

    public async Task SelectThreadAsync(ThreadInfo thread)
    {
        CurrentThread = thread;
        Messages.Clear();
        try
        {
            foreach (var message in await _client.ListMessagesAsync(thread.Uuid))
                Messages.Add(new ChatMessage(message.Role, message.Content, message.CreatedAt));
            StatusText = "Connected";
        }
        catch (Exception ex) { StatusText = $"Couldn't load messages: {ex.Message}"; }
    }

    public void StartNewThread()
    {
        if (IsBusy || CurrentWorkspace is null) return;
        CurrentThread = null;
        Messages.Clear();
        StatusText = "New conversation";
        OnPropertyChanged(nameof(HeaderTitle));
    }

    public Task SendAsync()
    {
        var content = ComposerText.Trim();
        if (string.IsNullOrWhiteSpace(content) || IsBusy || CurrentWorkspace is null || !_client.IsConnected) return Task.CompletedTask;
        Messages.Add(new ChatMessage("user", content));
        ComposerText = string.Empty;
        _streamingMessage = new ChatMessage("assistant", string.Empty);
        Messages.Add(_streamingMessage);
        IsBusy = true;
        _activeTurnId = _client.SendChat(CurrentThread?.Uuid, content, CurrentThread is null ? CurrentWorkspace.Uuid : null, SelectedModel?.Id);
        return Task.CompletedTask;
    }

    public void Stop() => _client.CancelChat(_activeTurnId);

    private void AppendDelta(ChatDeltaEventArgs change)
    {
        if (change.TurnId != _activeTurnId || _streamingMessage is null) return;
        _streamingMessage.AppendDelta(change.Delta);
    }

    private async Task CompleteTurnAsync(ChatDoneEventArgs completion)
    {
        if (completion.TurnId != _activeTurnId) return;
        FinishTurn("Connected");
        if (CurrentThread is null && CurrentWorkspace is { } workspace)
        {
            var threads = await _client.ListThreadsAsync(workspace.Uuid);
            if (threads.OrderByDescending(thread => thread.UpdatedAt).FirstOrDefault() is { } created) CurrentThread = created;
        }
        await ReloadThreadsAsync();
        OnPropertyChanged(nameof(HeaderTitle));
    }

    private async Task ReloadThreadsAsync()
    {
        if (CurrentWorkspace is not { } workspace) return;
        try
        {
            var currentId = CurrentThread?.Uuid;
            var updated = (await _client.ListThreadsAsync(workspace.Uuid)).OrderByDescending(thread => thread.UpdatedAt).ToList();
            Threads.Clear();
            foreach (var thread in updated) Threads.Add(thread);
            CurrentThread = updated.FirstOrDefault(thread => thread.Uuid == currentId) ?? CurrentThread;
        }
        catch (Exception ex) { StatusText = $"Couldn't refresh threads: {ex.Message}"; }
    }

    private void FinishTurn(string status)
    {
        _activeTurnId = null;
        _streamingMessage = null;
        IsBusy = false;
        StatusText = status;
    }

    partial void OnCurrentWorkspaceChanged(Workspace? value) => OnPropertyChanged(nameof(HeaderTitle));
    partial void OnCurrentThreadChanged(ThreadInfo? value) => OnPropertyChanged(nameof(HeaderTitle));
}
