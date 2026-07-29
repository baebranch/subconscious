using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>
/// The chat panel's view model: owns workspace/thread selection, the message list, and the
/// composer. Talks to the engine exclusively through <see cref="EngineClient"/> — no direct
/// database access, matching the "every client is a thin consumer of the Engine's API"
/// architecture principle (translation.md §3.3).
/// </summary>
public sealed partial class ChatViewModel : ViewModelBase
{
    private readonly EngineClient _client = new();
    private string? _activeTurnId;
    private string? _activeTurnThread;
    private MessageViewModel? _streamingAssistantBubble;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public ObservableCollection<ThreadInfo> Threads { get; } = [];
    public ObservableCollection<Workspace> Workspaces { get; } = [];

    [ObservableProperty]
    private string _statusText = "Connecting…";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _composerText = string.Empty;

    [ObservableProperty]
    private Workspace? _currentWorkspace;

    [ObservableProperty]
    private ThreadInfo? _currentThread;

    [ObservableProperty]
    private bool _isBusy;

    public async Task InitializeAsync(bool dev)
    {
        _client.ConnectionStatusChanged += (_, connected) =>
        {
            IsConnected = connected;
            StatusText = connected ? "Connected" : "Disconnected — reconnecting…";
        };
        _client.ChatDelta += OnChatDelta;
        _client.ChatDone += OnChatDone;
        _client.ChatError += OnChatError;

        try
        {
            await _client.ConnectAsync(dev);
            IsConnected = true;
            StatusText = "Connected";
            await RestoreAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to connect: {ex.Message}";
        }
    }

    private async Task RestoreAsync()
    {
        var workspaces = await _client.ListWorkspacesAsync();
        Workspaces.Clear();
        foreach (var ws in workspaces)
        {
            Workspaces.Add(ws);
        }

        var target = Workspaces.FirstOrDefault();
        if (target is null)
        {
            // No workspace exists yet on a fresh engine — create a default one so the
            // composer has somewhere to send the first message.
            target = await _client.CreateWorkspaceAsync("Default");
            Workspaces.Add(target);
        }

        await SelectWorkspaceAsync(target);
    }

    public async Task SelectWorkspaceAsync(Workspace workspace)
    {
        CurrentWorkspace = workspace;
        var threads = await _client.ListThreadsAsync(workspace.Uuid);
        Threads.Clear();
        foreach (var thread in threads)
        {
            Threads.Add(thread);
        }

        var target = Threads.FirstOrDefault();
        if (target is null)
        {
            target = await _client.CreateThreadAsync(workspace.Uuid, "New Thread");
            Threads.Add(target);
        }

        await SelectThreadAsync(target);
    }

    public async Task SelectThreadAsync(ThreadInfo thread)
    {
        CurrentThread = thread;
        var messages = await _client.ListMessagesAsync(thread.Uuid);
        Messages.Clear();
        foreach (var message in messages)
        {
            Messages.Add(new MessageViewModel(message.Role, message.Content));
        }
    }

    [RelayCommand]
    private async Task NewThreadAsync()
    {
        if (CurrentWorkspace is null)
        {
            return;
        }
        var thread = await _client.CreateThreadAsync(CurrentWorkspace.Uuid, "New Thread");
        Threads.Insert(0, thread);
        await SelectThreadAsync(thread);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        var text = ComposerText.Trim();
        if (text.Length == 0 || CurrentThread is null)
        {
            return;
        }

        Messages.Add(new MessageViewModel("user", text));
        ComposerText = string.Empty;

        _streamingAssistantBubble = new MessageViewModel("assistant", string.Empty);
        Messages.Add(_streamingAssistantBubble);

        IsBusy = true;
        _activeTurnThread = CurrentThread.Uuid;
        _activeTurnId = _client.SendChat(CurrentThread.Uuid, text);
    }

    private bool CanSend() => !IsBusy && CurrentThread is not null && ComposerText.Trim().Length > 0;

    partial void OnComposerTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnCurrentThreadChanged(ThreadInfo? value) => SendCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Stop()
    {
        _client.CancelChat(_activeTurnId);
    }

    private void OnChatDelta(object? sender, ChatDeltaEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid) || _streamingAssistantBubble is null)
        {
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _streamingAssistantBubble.AppendDelta(e.Delta));
    }

    private void OnChatDone(object? sender, ChatDoneEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid))
        {
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(ClearActiveTurn);
    }

    private void OnChatError(object? sender, ChatErrorEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid))
        {
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _streamingAssistantBubble?.AppendDelta($"\n\n⚠ {e.Error}");
            ClearActiveTurn();
        });
    }

    private void ClearActiveTurn()
    {
        _activeTurnId = null;
        _activeTurnThread = null;
        _streamingAssistantBubble = null;
        IsBusy = false;
    }

    private bool BelongsToActiveTurn(string? turnId, string threadUuid) =>
        threadUuid == _activeTurnThread && (turnId is null || turnId == _activeTurnId);

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}
