using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
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
    private bool _synchronizingSelectedModel;
    private string? _draftModelId;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];
    public ObservableCollection<ThreadInfo> Threads { get; } = [];
    public ObservableCollection<Workspace> Workspaces { get; } = [];
    public ObservableCollection<ModelInfo> AvailableModels { get; } = [];
    public ObservableCollection<WorkspaceSelectorItem> WorkspaceSelectorItems { get; } = [];

    /// <summary>True only for a persisted thread; local drafts cannot own an override.</summary>
    public bool IsThreadToolsAvailable => CurrentThread is not null && !IsBusy;
    public string ThreadToolsButtonText => CurrentThread is null ? "Tools (save thread first)" : "Tools";

    [ObservableProperty] private ToolPolicyEditorViewModel? _threadToolPolicy;
    [ObservableProperty] private bool _isThreadToolsOpen;
    [ObservableProperty] private bool _isThreadToolsLoading;
    [ObservableProperty] private bool _isThreadToolsSaving;
    [ObservableProperty] private bool _isThreadToolsDirty;
    [ObservableProperty] private string? _threadToolsError;

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

    /// <summary>Compact workspace context shown in the window title bar.</summary>
    public string WorkspaceIndicatorText => CurrentWorkspace is { } workspace
        ? $"Workspace: {workspace.Name}"
        : "Workspace: All workspaces";

    /// <summary>The workspace selected in the Threads context panel, suitable for the visible
    /// title context as well as the native window's text-only system title.</summary>
    public string ActiveWorkspaceName => CurrentWorkspace?.Name ?? "All workspaces";

    /// <summary>Workspace context embedded in the native text-only window title.</summary>
    public string TitleBarContextText => $"Subconscious — {ActiveWorkspaceName}";

    /// <summary>Text-only title for the taskbar and system menu. The visible MAUI caption renders
    /// its connection indicator as a coloured ellipse; native Windows title consumers retain this
    /// equivalent, accessible plain-text status.</summary>
    public string TitleBarText => $"{TitleBarContextText} — {StatusText}";

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

    [ObservableProperty]
    private ModelInfo? _selectedModel;

    /// <summary>Drafts keep their selected model locally until the first message creates a
    /// persisted thread, so this selector is available as soon as a workspace is active.</summary>
    public bool IsModelPickerEnabled => CurrentWorkspace is not null && !IsBusy && AvailableModels.Count > 0;

    partial void OnCurrentWorkspaceChanged(Workspace? value)
    {
        CurrentWorkspaceSelector = WorkspaceSelectorItems.FirstOrDefault(item => item.Workspace?.Uuid == value?.Uuid)
            ?? WorkspaceSelectorItems.FirstOrDefault();
        OnPropertyChanged(nameof(WorkspaceIndicatorText));
        OnPropertyChanged(nameof(ActiveWorkspaceName));
        OnPropertyChanged(nameof(TitleBarContextText));
        OnPropertyChanged(nameof(TitleBarText));
        SyncSelectedModel();
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(TitleBarText));
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(TitleBarText));

    partial void OnSelectedModelChanged(ModelInfo? value)
    {
        if (_synchronizingSelectedModel || value is null)
        {
            return;
        }

        if (CurrentThread is { } thread)
        {
            if (thread.DefaultModelId != value.Id)
            {
                _ = UpdateThreadModelAsync(thread.Uuid, value.Id);
            }
            return;
        }

        // The first-send path uses this temporary override directly. The Engine stores it while
        // materializing the draft, before executing the opening prompt.
        _draftModelId = value.Id;
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ToolApprovalRequestEventArgs? _pendingToolApproval;

    /// <summary>Whether a policy-protected tool call is awaiting an explicit user decision.</summary>
    public bool HasPendingToolApproval => PendingToolApproval is not null;

    partial void OnPendingToolApprovalChanged(ToolApprovalRequestEventArgs? value) =>
        OnPropertyChanged(nameof(HasPendingToolApproval));

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
        _client.ChatCancelled += OnChatCancelled;
        _client.ToolApprovalRequested += OnToolApprovalRequested;

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

        // The socket is usable only after the engine's hello acknowledgement. Leaving this false
        // until that event makes the title-bar dot accurately red during startup/reconnects.

        await LoadAvailableModelsAsync();
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

    private async Task LoadAvailableModelsAsync()
    {
        AvailableModels.Clear();
        try
        {
            var catalog = await _client.ListModelsAsync();
            foreach (var model in catalog)
            {
                AvailableModels.Add(model);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't load chat models: {ex.Message}";
            SyncSelectedModel();
            return;
        }

        try
        {
            // A saved configuration's stable ID—not its underlying provider model name—is the
            // selectable value. This keeps aliases/configurations distinct even when two entries
            // target the same model and lets the Engine resolve the selected credentials safely.
            var configurations = await _client.ListModelConfigurationsAsync();
            foreach (var configuration in configurations)
            {
                if (AvailableModels.Any(model => model.Id == configuration.Id))
                {
                    continue;
                }

                AvailableModels.Add(new ModelInfo
                {
                    Id = configuration.Id,
                    Name = string.IsNullOrWhiteSpace(configuration.Alias)
                        ? configuration.Model
                        : configuration.Alias,
                    Provider = configuration.Provider,
                    Description = configuration.BaseUrl,
                });
            }
        }
        catch (Exception ex)
        {
            // Do not silently make Echo appear to be the only usable model. In particular, an
            // older Engine has no model-configurations endpoint and returns a clear 404 here.
            // The built-in catalog remains usable, while the native title reports why saved
            // model entries were not offered for this session.
            StatusText = $"Configured models unavailable: {ex.Message}";
        }

        SyncSelectedModel();
    }

    private void SyncSelectedModel()
    {
        _synchronizingSelectedModel = true;
        try
        {
            var effectiveModelId = _draftModelId
                ?? CurrentThread?.DefaultModelId
                ?? CurrentWorkspace?.DefaultModelId;
            SelectedModel = AvailableModels.FirstOrDefault(model => model.Id == effectiveModelId)
                ?? AvailableModels.FirstOrDefault();
        }
        finally
        {
            _synchronizingSelectedModel = false;
        }

        OnPropertyChanged(nameof(IsModelPickerEnabled));
    }

    private async Task<bool> UpdateThreadModelAsync(string threadUuid, string modelId)
    {
        try
        {
            var updated = await _client.UpdateThreadModelAsync(threadUuid, modelId);
            var index = Threads.ToList().FindIndex(thread => thread.Uuid == updated.Uuid);
            if (index >= 0)
            {
                Threads[index] = updated;
            }

            if (CurrentThread?.Uuid == updated.Uuid)
            {
                CurrentThread = updated;
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't change model: {ex.Message}";
            SyncSelectedModel();
            return false;
        }
    }

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
        _draftModelId = null;
        CurrentThread = null;
        CurrentWorkspace = workspace;
        Messages.Clear();
        await RefreshThreadsAsync();

        var target = Threads.FirstOrDefault();
        if (target is not null)
        {
            await SelectThreadAsync(target);
        }
        else
        {
            // A workspace with no history opens as an unsaved local draft. The engine receives
            // no thread-create request until the user sends its first message.
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Clears the workspace filter and displays the threads from every workspace,
    /// newest first. The current thread remains selected when it is part of the aggregate list.</summary>
    public async Task ClearWorkspaceSelectionAsync()
    {
        _draftModelId = null;
        CurrentThread = null;
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
        _draftModelId = null;
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
        if (IsBusy)
        {
            return;
        }

        // "All workspaces" has no owner for a draft. Prefer the first available workspace so the
        // action always opens a usable composer instead of silently doing nothing.
        var workspace = CurrentWorkspace ?? Workspaces.FirstOrDefault();
        if (workspace is null)
        {
            return;
        }

        if (CurrentWorkspace?.Uuid != workspace.Uuid)
        {
            CurrentWorkspace = workspace;
            await RefreshThreadsAsync();
        }

        // A null thread is the explicit local-draft state: blank transcript, no title, and no
        // backend write. The user can immediately choose a local model override for this draft.
        _draftModelId = null;
        CurrentThread = null;
        Messages.Clear();
        // CurrentThread can already be null when replacing one local draft with another, so its
        // generated change hook may not run. Explicitly initialize the draft Picker every time.
        SyncSelectedModel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Used by workspace and thread editors to render actual engine catalog groups.</summary>
    public Task<ToolCatalog> GetToolCatalogAsync(CancellationToken cancellationToken = default) =>
        _client.GetToolCatalogAsync(cancellationToken);

    public Task<ToolConfigResponse> GetWorkspaceToolsConfigAsync(string uuid, CancellationToken cancellationToken = default) =>
        _client.GetWorkspaceToolsConfigAsync(uuid, cancellationToken);

    /// <summary>Creates a workspace and keeps the in-memory selector synchronized.</summary>
    public async Task<Workspace> CreateWorkspaceEntryAsync(CreateWorkspaceRequest request)
    {
        var workspace = await _client.CreateWorkspaceAsync(request);
        Workspaces.Add(workspace);
        RebuildWorkspaceSelectorItems();
        return workspace;
    }

    public Task<Workspace> CreateWorkspaceEntryAsync(string name, string? description, string? defaultModelId) =>
        CreateWorkspaceEntryAsync(new CreateWorkspaceRequest { Name = name, Description = description, DefaultModelId = defaultModelId });

    /// <summary>Persists all workspace fields and updates the immutable wire record in the active lists.</summary>
    public async Task<Workspace> UpdateWorkspaceEntryAsync(string uuid, CreateWorkspaceRequest request)
    {
        var updated = await _client.UpdateWorkspaceAsync(uuid, request);

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

    public Task<Workspace> UpdateWorkspaceEntryAsync(string uuid, string name, string? description, string? defaultModelId) =>
        UpdateWorkspaceEntryAsync(uuid, new CreateWorkspaceRequest { Name = name, Description = description, DefaultModelId = defaultModelId });

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        var text = ComposerText.Trim();
        var threadUuid = CurrentThread?.Uuid;
        var workspaceUuid = threadUuid is null ? CurrentWorkspace?.Uuid : null;
        if (text.Length == 0 || (threadUuid is null && workspaceUuid is null))
        {
            return;
        }

        Messages.Add(new MessageViewModel("user", text));
        ComposerText = string.Empty;

        _streamingAssistantBubble = new MessageViewModel("assistant", string.Empty);
        Messages.Add(_streamingAssistantBubble);

        IsBusy = true;
        _activeTurnThread = threadUuid;
        // For a local draft, the temporary selection is the source of truth for the opening
        // prompt. The Engine stores it on the thread before executing that first turn.
        var modelId = threadUuid is null ? _draftModelId ?? SelectedModel?.Id : SelectedModel?.Id;
        _activeTurnId = _client.SendChat(threadUuid, text, workspaceUuid, modelId);
    }

    private bool CanSend() => IsConnected
        && !IsBusy
        && ComposerText.Trim().Length > 0
        && (CurrentThread is not null || CurrentWorkspace is not null);

    partial void OnComposerTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
        OpenThreadToolsCommand.NotifyCanExecuteChanged();
        UseWorkspaceToolDefaultsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsModelPickerEnabled));
        OnPropertyChanged(nameof(IsThreadToolsAvailable));
    }

    partial void OnCurrentThreadChanged(ThreadInfo? value)
    {
        if (IsThreadToolsOpen && ThreadToolPolicy is not null)
        {
            CloseThreadTools();
        }
        SyncSelectedModel();
        SendCommand.NotifyCanExecuteChanged();
        OpenThreadToolsCommand.NotifyCanExecuteChanged();
        UseWorkspaceToolDefaultsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsThreadToolsAvailable));
        OnPropertyChanged(nameof(ThreadToolsButtonText));
    }

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

        // The engine's first delta resolves a local draft to its persisted thread UUID. No UI
        // thread state is changed here; the completed turn refreshes history and applies the
        // engine-assigned title after the assistant message has been saved.
        _activeTurnThread ??= e.ThreadUuid;
        MainThread.BeginInvokeOnMainThread(() => _streamingAssistantBubble.AppendDelta(e.Delta));
    }

    private void OnChatDone(object? sender, ChatDoneEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid))
        {
            return;
        }

        var completedThreadUuid = _activeTurnThread ?? e.ThreadUuid;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ClearActiveTurn();
            _ = CompleteTurnAsync(completedThreadUuid);
        });
    }

    private async Task CompleteTurnAsync(string threadUuid)
    {
        try
        {
            await RefreshThreadsAsync();
            if (CurrentThread is null
                && Threads.FirstOrDefault(thread => thread.Uuid == threadUuid) is { } materializedThread)
            {
                // The Engine has already persisted the draft model on the newly created thread.
                // Clear the temporary override before joining that identity to the streamed UI.
                _draftModelId = null;
                CurrentThread = materializedThread;
            }

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't refresh thread: {ex.Message}";
        }
    }

    private void OnChatCancelled(object? sender, ChatCancelledEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_streamingAssistantBubble is { Content.Length: 0 } pending)
            {
                Messages.Remove(pending);
            }
            PendingToolApproval = null;
            ClearActiveTurn();
        });
    }

    private void OnToolApprovalRequested(object? sender, ToolApprovalRequestEventArgs e)
    {
        if (!BelongsToActiveTurn(e.TurnId, e.ThreadUuid))
        {
            return;
        }
        MainThread.BeginInvokeOnMainThread(() => PendingToolApproval = e);
    }

    [RelayCommand]
    private void ApproveTool()
    {
        if (PendingToolApproval is not { } request)
        {
            return;
        }
        _client.ResolveToolApproval(request.TurnId, request.ApprovalId, approve: true);
        PendingToolApproval = null;
    }

    [RelayCommand]
    private void DenyTool()
    {
        if (PendingToolApproval is not { } request)
        {
            return;
        }
        _client.ResolveToolApproval(request.TurnId, request.ApprovalId, approve: false);
        PendingToolApproval = null;
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
        PendingToolApproval = null;
        IsBusy = false;
    }

    private bool BelongsToActiveTurn(string? turnId, string threadUuid) =>
        (string.IsNullOrEmpty(_activeTurnThread) || threadUuid == _activeTurnThread)
        && (turnId is null || turnId == _activeTurnId);

    [RelayCommand(CanExecute = nameof(CanOpenThreadTools))]
    private async Task OpenThreadToolsAsync()
    {
        if (CurrentThread is not { } thread)
        {
            return;
        }

        IsThreadToolsOpen = true;
        IsThreadToolsLoading = true;
        ThreadToolsError = null;
        IsThreadToolsDirty = false;
        try
        {
            var editor = new ToolPolicyEditorViewModel();
            editor.Changed += (_, _) =>
            {
                IsThreadToolsDirty = true;
                SaveThreadToolsCommand.NotifyCanExecuteChanged();
            };
            ThreadToolPolicy = editor;
            var catalog = await _client.GetToolCatalogAsync();
            var effective = await _client.GetThreadToolsConfigAsync(thread.Uuid);
            editor.Populate(catalog, effective.Config);
        }
        catch (Exception exception)
        {
            ThreadToolsError = $"Couldn't load thread tools: {exception.Message}";
        }
        finally
        {
            IsThreadToolsLoading = false;
        }
    }

    private bool CanOpenThreadTools() => IsThreadToolsAvailable;
    private bool CanUseWorkspaceToolDefaults() => CurrentThread is not null && ThreadToolPolicy is not null && !IsThreadToolsLoading && !IsThreadToolsSaving;
    private bool CanSaveThreadTools() => CurrentThread is not null && ThreadToolPolicy is not null && IsThreadToolsDirty && !IsThreadToolsSaving;

    [RelayCommand(CanExecute = nameof(CanSaveThreadTools))]
    private async Task SaveThreadToolsAsync()
    {
        if (CurrentThread is not { } thread || ThreadToolPolicy is null)
        {
            return;
        }

        IsThreadToolsSaving = true;
        ThreadToolsError = null;
        try
        {
            var saved = await _client.UpdateThreadToolsConfigAsync(thread.Uuid, ThreadToolPolicy.SerializeDesiredConfig());
            ThreadToolPolicy.Populate(await _client.GetToolCatalogAsync(), saved.Config);
            IsThreadToolsDirty = false;
        }
        catch (Exception exception)
        {
            ThreadToolsError = $"Couldn't save thread tools: {exception.Message}";
        }
        finally
        {
            IsThreadToolsSaving = false;
            SaveThreadToolsCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseWorkspaceToolDefaults))]
    private async Task UseWorkspaceToolDefaultsAsync()
    {
        if (CurrentThread is not { } thread || ThreadToolPolicy is null)
        {
            return;
        }

        IsThreadToolsSaving = true;
        ThreadToolsError = null;
        try
        {
            await _client.DeleteThreadToolsConfigAsync(thread.Uuid);
            var effective = await _client.GetThreadToolsConfigAsync(thread.Uuid);
            ThreadToolPolicy.Populate(await _client.GetToolCatalogAsync(), effective.Config);
            IsThreadToolsDirty = false;
        }
        catch (Exception exception)
        {
            ThreadToolsError = $"Couldn't restore workspace defaults: {exception.Message}";
        }
        finally
        {
            IsThreadToolsSaving = false;
            SaveThreadToolsCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsThreadToolsDirtyChanged(bool value) => SaveThreadToolsCommand.NotifyCanExecuteChanged();
    partial void OnIsThreadToolsSavingChanged(bool value)
    {
        SaveThreadToolsCommand.NotifyCanExecuteChanged();
        UseWorkspaceToolDefaultsCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsThreadToolsLoadingChanged(bool value) => UseWorkspaceToolDefaultsCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void CloseThreadTools()
    {
        IsThreadToolsOpen = false;
        IsThreadToolsDirty = false;
        ThreadToolsError = null;
        ThreadToolPolicy = null;
        SaveThreadToolsCommand.NotifyCanExecuteChanged();
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}
