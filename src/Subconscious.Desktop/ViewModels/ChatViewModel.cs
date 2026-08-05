using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>One row in the Threads header's native workspace selector. A null workspace is the
/// deliberate, selectable "All workspaces" state rather than an incidental empty Picker.</summary>
public sealed record WorkspaceSelectorItem(string DisplayName, Workspace? Workspace);

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
    private long _themeRevision;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public ObservableCollection<ThreadInfo> Threads { get; } = [];
    public ObservableCollection<Workspace> Workspaces { get; } = [];
    public ObservableCollection<WorkspaceSelectorItem> WorkspaceSelectorItems { get; } = [];

    /// <summary>Raised after a workspace/thread selection has completed and is ready to persist.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Incremented after semantic resources are replaced so consumers such as the
    /// WebView transcript can explicitly regenerate their captured palette.</summary>
    public long ThemeRevision => _themeRevision;

    /// <summary>Theme-aware icon color for nested MauiIcons markup in the chat view.</summary>
    public Color IconColor => Application.Current?.Resources.TryGetValue("PrimaryTextColor", out var value) == true
        && value is Color color
        ? color
        : Colors.Black;

    /// <summary>Raises the icon binding and the HTML-transcript palette revision after
    /// ThemeService replaces semantic runtime colors.</summary>
    public void RefreshTheme()
    {
        _themeRevision++;
        OnPropertyChanged(nameof(ThemeRevision));
        OnPropertyChanged(nameof(IconColor));
    }

    [ObservableProperty]
    private string _statusText = "Connecting…";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _composerText = string.Empty;

    [ObservableProperty]
    private Workspace? _currentWorkspace;

    [ObservableProperty]
    private WorkspaceSelectorItem? _currentWorkspaceSelector;

    [ObservableProperty]
    private ThreadInfo? _currentThread;

    partial void OnCurrentWorkspaceChanged(Workspace? value)
    {
        CurrentWorkspaceSelector = WorkspaceSelectorItems.FirstOrDefault(item => item.Workspace?.Uuid == value?.Uuid)
            ?? WorkspaceSelectorItems.FirstOrDefault();
    }

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>True while <see cref="LoadWorkspacesCommand"/> is in flight, so the Workspaces
    /// panel can show progress instead of its "no workspaces yet" empty state — those two look
    /// identical otherwise, and the empty one is a lie during the initial load.</summary>
    [ObservableProperty]
    private bool _isLoadingWorkspaces;

    /// <summary>Why the last workspace load failed, or null if it didn't. Bound by the Workspaces
    /// panel next to a Retry button: a failed list used to leave the panel reading "No workspaces
    /// yet", which is indistinguishable from an engine that has none.</summary>
    [ObservableProperty]
    private string? _workspacesError;

    public async Task InitializeAsync(
        bool dev,
        int? activeWorkspaceId = null,
        int? selectedThreadId = null,
        bool showAllThreads = false,
        bool restoreSelection = false)
    {
        // Raised from the WebSocket receive loop, so marshal to the UI thread before touching
        // bound properties.
        _client.ConnectionStatusChanged += (_, connected) => MainThread.BeginInvokeOnMainThread(() =>
        {
            IsConnected = connected;
            StatusText = connected ? "Connected" : "Disconnected — reconnecting…";
        });
        _client.ChatDelta += OnChatDelta;
        _client.ChatDone += OnChatDone;
        _client.ChatError += OnChatError;

        try
        {
            await _client.ConnectAsync(dev);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to connect: {ex.Message}";
            WorkspacesError = $"Can't reach the engine. {ex.Message}";
            return;
        }

        IsConnected = true;
        StatusText = "Connected";

        await LoadWorkspacesCoreAsync(activateInitialSelection: false);
        if (!restoreSelection
            || !await RestoreSelectionAsync(activeWorkspaceId, selectedThreadId, showAllThreads))
        {
            await ActivateInitialWorkspaceAsync();
        }
    }

    /// <summary>
    /// Loads the workspace list from the engine into <see cref="Workspaces"/> — run once on
    /// startup and again from the Workspaces panel's Retry button.
    ///
    /// Listing and activating are deliberately two steps with separate error handling: the list is
    /// what the Workspaces panel renders, and a failure further along (no threads, an unreachable
    /// thread endpoint) must not blank out a list that loaded perfectly well.
    /// </summary>
    [RelayCommand]
    private Task LoadWorkspacesAsync() => LoadWorkspacesCoreAsync(activateInitialSelection: true);

    private async Task LoadWorkspacesCoreAsync(bool activateInitialSelection)
    {
        IsLoadingWorkspaces = true;
        WorkspacesError = null;
        try
        {
            var workspaces = await _client.ListWorkspacesAsync();
            Workspaces.Clear();
            foreach (var ws in workspaces)
            {
                Workspaces.Add(ws);
            }
            RebuildWorkspaceSelectorItems();
        }
        catch (Exception ex)
        {
            WorkspacesError = ex.Message;
            return;
        }
        finally
        {
            IsLoadingWorkspaces = false;
        }

        if (activateInitialSelection)
        {
            await ActivateInitialWorkspaceAsync();
        }
    }

    /// <summary>Rebuilds the native Picker's choices after a workspace mutation. The first row is
    /// an explicit no-filter state, so users can return to the all-workspaces thread history.</summary>
    private void RebuildWorkspaceSelectorItems()
    {
        WorkspaceSelectorItems.Clear();
        WorkspaceSelectorItems.Add(new WorkspaceSelectorItem("All workspaces", null));
        foreach (var workspace in Workspaces)
        {
            WorkspaceSelectorItems.Add(new WorkspaceSelectorItem(workspace.Name, workspace));
        }

        CurrentWorkspaceSelector = WorkspaceSelectorItems.FirstOrDefault(item => item.Workspace?.Uuid == CurrentWorkspace?.Uuid)
            ?? WorkspaceSelectorItems.FirstOrDefault();
    }

    /// <summary>Picks the workspace the chat pane should open on and selects it. Keeps the current
    /// one if it survived a reload, so Retry doesn't yank the user back to the first entry.</summary>
    private async Task ActivateInitialWorkspaceAsync()
    {
        try
        {
            var target = Workspaces.FirstOrDefault(w => w.Uuid == CurrentWorkspace?.Uuid)
                ?? Workspaces.FirstOrDefault();

            if (target is null)
            {
                // No workspace exists yet on a fresh engine — create a default one so the
                // composer has somewhere to send the first message.
                target = await _client.CreateWorkspaceAsync("Default");
                Workspaces.Add(target);
                RebuildWorkspaceSelectorItems();
            }

            await SelectWorkspaceAsync(target);
        }
        catch (Exception ex)
        {
            StatusText = $"Connected — couldn't open a thread: {ex.Message}";
        }
    }

    /// <summary>Loads the active workspace's thread history, or aggregates every workspace when
    /// none is active. The engine already orders scoped results by UpdatedAt; sorting again here
    /// also gives the all-workspaces result one consistent newest-first order.</summary>
    public async Task RefreshThreadsAsync()
    {
        IEnumerable<ThreadInfo> threads;
        if (CurrentWorkspace is { } workspace)
        {
            threads = await _client.ListThreadsAsync(workspace.Uuid);
        }
        else
        {
            var batches = await Task.WhenAll(Workspaces.Select(workspace => _client.ListThreadsAsync(workspace.Uuid)));
            threads = batches.SelectMany(batch => batch);
        }

        var orderedThreads = threads.OrderByDescending(thread => thread.UpdatedAt).ToList();
        var activeThreadUuid = CurrentThread?.Uuid;

        Threads.Clear();
        foreach (var thread in orderedThreads)
        {
            Threads.Add(thread);
        }

        // Refreshing replaces immutable wire records. Keep the current thread pointing at its
        // refreshed record so its title and timestamp stay in step with the list.
        if (activeThreadUuid is not null
            && orderedThreads.FirstOrDefault(thread => thread.Uuid == activeThreadUuid) is { } refreshedCurrentThread)
        {
            CurrentThread = refreshedCurrentThread;
        }
    }

    /// <summary>Changes the active workspace and loads its newest-first thread history.</summary>
    public async Task SelectWorkspaceAsync(Workspace workspace)
    {
        CurrentWorkspace = workspace;
        CurrentThread = null;
        Messages.Clear();
        await RefreshThreadsAsync();

        var target = Threads.FirstOrDefault();
        if (target is null)
        {
            await _client.CreateThreadAsync(workspace.Uuid, "New Thread");
            await RefreshThreadsAsync();
            target = Threads.FirstOrDefault();
        }

        if (target is not null)
        {
            await SelectThreadAsync(target);
        }
        else
        {
            // A successful workspace selection is still durable even if thread creation did not
            // yield a selectable row.
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Clears the workspace filter and displays the threads from every workspace,
    /// newest first. The current thread remains selected when it is part of the aggregate list.</summary>
    public async Task ClearWorkspaceSelectionAsync()
    {
        CurrentWorkspace = null;
        await RefreshThreadsAsync();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies a persisted workspace filter and selected thread after the workspace
    /// list has loaded. Returns false only when the saved workspace no longer exists or loading
    /// the saved selection failed, so callers can deliberately fall back to the first workspace.</summary>
    public async Task<bool> RestoreSelectionAsync(int? activeWorkspaceId, int? selectedThreadId, bool showAllThreads)
    {
        try
        {
            if (showAllThreads)
            {
                await ClearWorkspaceSelectionAsync();
            }
            else if (Workspaces.FirstOrDefault(workspace => workspace.Id == activeWorkspaceId) is { } workspace)
            {
                await SelectWorkspaceAsync(workspace);
            }
            else
            {
                return false;
            }

            if (Threads.FirstOrDefault(thread => thread.Id == selectedThreadId) is { } thread)
            {
                await SelectThreadAsync(thread);
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Connected — couldn't restore chat state: {ex.Message}";
            return false;
        }
    }

    /// <summary>Selects a thread immediately so its context row highlights, then replaces the
    /// chat panel with its persisted history in chronological order.</summary>
    [RelayCommand]
    public async Task SelectThreadAsync(ThreadInfo thread)
    {
        CurrentThread = thread;
        Messages.Clear();

        try
        {
            var messages = await _client.ListMessagesAsync(thread.Uuid);
            foreach (var message in messages)
            {
                Messages.Add(new MessageViewModel(message.Role, message.Content, message.CreatedAt));
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't load thread: {ex.Message}";
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task NewThreadAsync()
    {
        if (CurrentWorkspace is null)
        {
            return;
        }

        var createdThread = await _client.CreateThreadAsync(CurrentWorkspace.Uuid, "New Thread");
        await RefreshThreadsAsync();

        if (Threads.FirstOrDefault(thread => thread.Uuid == createdThread.Uuid) is { } thread)
        {
            await SelectThreadAsync(thread);
        }
    }

    /// <summary>Creates a workspace from the Workspaces panel's create form and adds it to the
    /// list (does not switch the active chat workspace — the user does that explicitly by
    /// clicking the new entry).</summary>
    public async Task<Workspace> CreateWorkspaceEntryAsync(string name, string? description, string? defaultModelId)
    {
        var workspace = await _client.CreateWorkspaceAsync(name, description, defaultModelId);
        Workspaces.Add(workspace);
        RebuildWorkspaceSelectorItems();
        return workspace;
    }

    /// <summary>Persists edits from the Workspaces panel's details form, updating the in-memory
    /// list (and <see cref="CurrentWorkspace"/> if it's the one being edited) in place.</summary>
    public async Task<Workspace> UpdateWorkspaceEntryAsync(string uuid, string name, string? description, string? defaultModelId)
    {
        var updated = await _client.UpdateWorkspaceAsync(uuid, name, description, defaultModelId);

        var index = Workspaces.ToList().FindIndex(w => w.Uuid == uuid);
        if (index >= 0)
        {
            Workspaces[index] = updated;
        }

        if (CurrentWorkspace?.Uuid == uuid)
        {
            CurrentWorkspace = updated;
        }

        RebuildWorkspaceSelectorItems();
        return updated;
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
        // Engine events arrive on the WebSocket receive loop; bubble updates have to hop back to
        // the UI thread (Avalonia's Dispatcher.UIThread.Post equivalent in MAUI).
        MainThread.BeginInvokeOnMainThread(() => _streamingAssistantBubble.AppendDelta(e.Delta));
    }

    private void OnChatDone(object? sender, ChatDoneEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid))
        {
            return;
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ClearActiveTurn();
            _ = RefreshThreadsAsync();
        });
    }

    private void OnChatError(object? sender, ChatErrorEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid))
        {
            return;
        }
        MainThread.BeginInvokeOnMainThread(() =>
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
