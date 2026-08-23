using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Subconscious.Chat;
using Subconscious.Mobile.Engine;

namespace Subconscious.Mobile;

public enum MobileContextSection { Threads, Files, Workspaces, Settings, Account }
public enum MobileMainContent { Chat, WorkspaceSettings, Files, GeneralSettings, ModelsSettings, ToolsSettings, SkillsSettings, AboutSettings, Account }
public enum MobileSettingsPage { General, Models, Tools, Skills, About }

/// <summary>Shared phone chat and navigation state. The Shell flyout and main page consume one source of truth.</summary>
public sealed partial class MobileChatSession : ObservableObject
{
    private readonly EngineClient _client;
    private readonly PairedEngineStore _pairedEngineStore;
    private readonly WorkspaceStore _workspaceStore;
    private readonly MobileAppearancePreferences _appearancePreferences;
    private bool _initialized;
    private string? _activeTurnId;
    private ChatMessage? _streamingMessage;
    private string? _workspaceToolsConfig;
    private string? _workspaceApprovalConfig;
    private string? _workspaceRagConfig;

#if SUBCONSCIOUS_LOCAL_ENGINE
    private static bool UseDevelopmentEngine => true;
#else
    private static bool UseDevelopmentEngine => false;
#endif

    public MobileChatSession(
        EngineClient client,
        PairedEngineStore pairedEngineStore,
        WorkspaceStore workspaceStore,
        MobileAppearancePreferences appearancePreferences)
    {
        _client = client;
        _pairedEngineStore = pairedEngineStore;
        _workspaceStore = workspaceStore;
        _appearancePreferences = appearancePreferences;
        _appearancePreferences.AppearanceChanged += (_, _) => MainThread.BeginInvokeOnMainThread(
            () => AppearanceThemeRevision++);
    }

    public ObservableCollection<IChatTranscriptMessage> Messages { get; } = [];
    public ObservableCollection<Workspace> Workspaces => _workspaceStore.Workspaces;
    public ObservableCollection<ThreadInfo> Threads { get; } = [];
    public ObservableCollection<ModelInfo> AvailableModels { get; } = [];
    [ObservableProperty] private long _appearanceThemeRevision;
    public IReadOnlyList<string> AppearancePaletteOptions => _appearancePreferences.PaletteOptions;
    public IReadOnlyList<string> LightingModeOptions => _appearancePreferences.LightingOptions;

    public string SelectedAppearancePalette
    {
        get => _appearancePreferences.Palette;
        set
        {
            if (string.Equals(value, _appearancePreferences.Palette, StringComparison.Ordinal)) return;
            _appearancePreferences.SetPalette(value);
            OnPropertyChanged();
        }
    }

    public string SelectedLightingMode
    {
        get => _appearancePreferences.LightingMode;
        set
        {
            if (string.Equals(value, _appearancePreferences.LightingMode, StringComparison.Ordinal)) return;
            _appearancePreferences.SetLightingMode(value);
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private Workspace? _currentWorkspace;
    [ObservableProperty] private ThreadInfo? _currentThread;
    [ObservableProperty] private ModelInfo? _selectedModel;
    [ObservableProperty] private string _composerText = string.Empty;
    [ObservableProperty] private string _statusText = "Connecting…";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _pairingInvitation = string.Empty;
    [ObservableProperty] private string? _pairingError;
    [ObservableProperty] private string? _pairedEngineDescription;
    [ObservableProperty] private bool _hasPairedEngine;
    [ObservableProperty] private bool _isPairing;

    [ObservableProperty] private MobileContextSection _currentContextSection = MobileContextSection.Threads;
    [ObservableProperty] private MobileMainContent _currentMainContent = MobileMainContent.Chat;
    [ObservableProperty] private MobileSettingsPage _selectedSettingsPage = MobileSettingsPage.General;
    [ObservableProperty] private Workspace? _selectedWorkspaceForSettings;
    [ObservableProperty] private string _workspaceSettingsName = string.Empty;
    [ObservableProperty] private string? _workspaceSettingsDescription;
    [ObservableProperty] private ModelInfo? _workspaceSettingsDefaultModel;
    [ObservableProperty] private string _workspaceDirectoriesText = string.Empty;
    [ObservableProperty] private bool _workspaceBuildKnowledgeGraph;
    [ObservableProperty] private bool _workspaceRequireApprovalForQueries = true;
    [ObservableProperty] private bool _workspaceRequireApprovalForMutations = true;
    [ObservableProperty] private string? _workspaceSettingsError;
    [ObservableProperty] private string? _workspaceSettingsStatus;
    [ObservableProperty] private bool _isSavingWorkspace;

    public string HeaderTitle => CurrentThread?.Title ?? CurrentWorkspace?.Name ?? "Subconscious";
    public string WorkspaceSettingsTitle => SelectedWorkspaceForSettings?.Name ?? "Workspace settings";
    public string ConnectionDescription => HasPairedEngine
        ? $"Paired LAN engine: {PairedEngineDescription}"
        : UseDevelopmentEngine
            ? "Development engine connection (Android emulators use the host through 10.0.2.2)."
            : "No paired engine. Open Account to pair a LAN engine.";
    public string AboutVersion => $"Version {AppInfo.Current.VersionString}";

    public bool IsThreadsContext => CurrentContextSection == MobileContextSection.Threads;
    public bool IsFilesContext => CurrentContextSection == MobileContextSection.Files;
    public bool IsWorkspacesContext => CurrentContextSection == MobileContextSection.Workspaces;
    public bool IsSettingsContext => CurrentContextSection == MobileContextSection.Settings;
    public bool IsAccountContext => CurrentContextSection == MobileContextSection.Account;

    public bool IsChatOpen => CurrentMainContent == MobileMainContent.Chat;
    public bool IsWorkspaceSettingsOpen => CurrentMainContent == MobileMainContent.WorkspaceSettings;
    public bool IsFilesOpen => CurrentMainContent == MobileMainContent.Files;
    public bool IsGeneralSettingsOpen => CurrentMainContent == MobileMainContent.GeneralSettings;
    public bool IsModelsSettingsOpen => CurrentMainContent == MobileMainContent.ModelsSettings;
    public bool IsToolsSettingsOpen => CurrentMainContent == MobileMainContent.ToolsSettings;
    public bool IsSkillsSettingsOpen => CurrentMainContent == MobileMainContent.SkillsSettings;
    public bool IsAboutSettingsOpen => CurrentMainContent == MobileMainContent.AboutSettings;
    public bool IsAccountOpen => CurrentMainContent == MobileMainContent.Account;

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
            if (UseDevelopmentEngine)
            {
                // Debug builds always try the emulator/local development engine first. Only when
                // that is unavailable do they fall back to an explicitly paired LAN endpoint.
                SetPairedEndpoint(null);
                try
                {
                    await ConnectAndLoadAsync(null);
                    return;
                }
                catch (Exception localException)
                {
                    var pairedEndpoint = await _pairedEngineStore.LoadAsync();
                    if (pairedEndpoint is null)
                    {
                        throw new InvalidOperationException(
                            "Couldn't reach the local development engine and no LAN engine is paired.",
                            localException);
                    }

                    SetPairedEndpoint(pairedEndpoint);
                    await ConnectAndLoadAsync(pairedEndpoint);
                    return;
                }
            }

            else
            {
                var endpoint = await _pairedEngineStore.LoadAsync();
                SetPairedEndpoint(endpoint);
                await ConnectAndLoadAsync(endpoint);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Can't reach the engine: {ex.Message}";
        }
    }

    public async Task PairEngineAsync()
    {
        if (IsPairing) return;
        IsPairing = true;
        PairingError = null;
        try
        {
            var endpoint = EnginePairingInvitation.Parse(PairingInvitation);
            await _pairedEngineStore.SaveAsync(endpoint);
            SetPairedEndpoint(endpoint);
            await ConnectAndLoadAsync(endpoint);
            PairingInvitation = string.Empty;
            StatusText = $"Connected to {endpoint.DisplayName}.";
        }
        catch (Exception exception)
        {
            PairingError = $"Couldn't pair engine: {exception.Message}";
        }
        finally
        {
            IsPairing = false;
        }
    }

    public async Task ForgetPairedEngineAsync()
    {
        _pairedEngineStore.Remove();
        await _client.DisconnectAsync();
        SetPairedEndpoint(null);
        PairingError = null;
        StatusText = "Paired engine removed.";
    }

    private async Task ConnectAndLoadAsync(EngineEndpoint? endpoint)
    {
        if (endpoint is null)
        {
            await _client.ConnectAsync(dev: UseDevelopmentEngine);
        }
        else
        {
            await _client.ConnectAsync(endpoint);
        }

        await _workspaceStore.RefreshConnectedAsync();
        AvailableModels.Clear();
        foreach (var model in await _client.ListModelsAsync()) AvailableModels.Add(model);
        SelectedModel = AvailableModels.FirstOrDefault();
        if (Workspaces.FirstOrDefault() is { } workspace) await SelectWorkspaceAsync(workspace);
        else StatusText = "Connected — create a workspace on Desktop to begin.";
    }

    private void SetPairedEndpoint(EngineEndpoint? endpoint)
    {
        HasPairedEngine = endpoint is not null;
        PairedEngineDescription = endpoint?.DisplayName;
        OnPropertyChanged(nameof(ConnectionDescription));
    }

    public async Task RefreshAsync()
    {
        await _workspaceStore.RefreshAsync();
        if (CurrentWorkspace is null && Workspaces.FirstOrDefault() is { } workspace) await SelectWorkspaceAsync(workspace);
    }

    public void SelectContext(MobileContextSection section) => CurrentContextSection = section;

    public void OpenChat() => CurrentMainContent = MobileMainContent.Chat;

    public void OpenFiles() => CurrentMainContent = MobileMainContent.Files;

    public void OpenAccount() => CurrentMainContent = MobileMainContent.Account;

    public void OpenSettings(MobileSettingsPage page)
    {
        SelectedSettingsPage = page;
        CurrentMainContent = page switch
        {
            MobileSettingsPage.Models => MobileMainContent.ModelsSettings,
            MobileSettingsPage.Tools => MobileMainContent.ToolsSettings,
            MobileSettingsPage.Skills => MobileMainContent.SkillsSettings,
            MobileSettingsPage.About => MobileMainContent.AboutSettings,
            _ => MobileMainContent.GeneralSettings,
        };
    }

    /// <summary>Opens workspace management without changing the workspace that filters chat threads.</summary>
    public void OpenWorkspaceSettings(Workspace workspace)
    {
        SelectedWorkspaceForSettings = workspace;
        WorkspaceSettingsName = workspace.Name;
        WorkspaceSettingsDescription = workspace.Description;
        WorkspaceSettingsDefaultModel = AvailableModels.FirstOrDefault(model => model.Id == workspace.DefaultModelId)
            ?? AvailableModels.FirstOrDefault();
        WorkspaceDirectoriesText = string.Join(Environment.NewLine, ReadStringList(workspace.Directories));
        _workspaceToolsConfig = workspace.ToolsConfig;
        _workspaceApprovalConfig = workspace.ApprovalConfig;
        _workspaceRagConfig = workspace.RagConfig;
        WorkspaceBuildKnowledgeGraph = ReadBool(workspace.RagConfig, "semantic_graph", false);
        WorkspaceRequireApprovalForQueries = ReadBool(workspace.ApprovalConfig, "query", true);
        WorkspaceRequireApprovalForMutations = ReadBool(workspace.ApprovalConfig, "mutation", true);
        WorkspaceSettingsError = null;
        WorkspaceSettingsStatus = null;
        CurrentMainContent = MobileMainContent.WorkspaceSettings;
        OnPropertyChanged(nameof(WorkspaceSettingsTitle));
    }

    public async Task SaveWorkspaceSettingsAsync()
    {
        var workspace = SelectedWorkspaceForSettings;
        var name = WorkspaceSettingsName.Trim();
        if (workspace is null || name.Length == 0 || IsSavingWorkspace)
        {
            WorkspaceSettingsError = workspace is null ? "Choose a workspace first." : "Name is required.";
            return;
        }

        IsSavingWorkspace = true;
        WorkspaceSettingsError = null;
        WorkspaceSettingsStatus = null;
        try
        {
            var directories = WorkspaceDirectoriesText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var request = new CreateWorkspaceRequest
            {
                Name = name,
                Description = string.IsNullOrWhiteSpace(WorkspaceSettingsDescription)
                    ? null : WorkspaceSettingsDescription.Trim(),
                DefaultModelId = WorkspaceSettingsDefaultModel?.Id,
                ToolsConfig = _workspaceToolsConfig,
                Directories = JsonSerializer.Serialize(directories),
                ApprovalConfig = SetBooleans(_workspaceApprovalConfig,
                    ("query", WorkspaceRequireApprovalForQueries),
                    ("mutation", WorkspaceRequireApprovalForMutations)),
                RagConfig = SetBooleans(_workspaceRagConfig,
                    ("semantic_graph", WorkspaceBuildKnowledgeGraph)),
            };
            var updated = await _client.UpdateWorkspaceAsync(workspace.Uuid, request);
            _workspaceStore.Replace(updated);
            SelectedWorkspaceForSettings = updated;
            if (CurrentWorkspace?.Uuid == updated.Uuid)
            {
                CurrentWorkspace = updated;
            }
            _workspaceApprovalConfig = updated.ApprovalConfig;
            _workspaceRagConfig = updated.RagConfig;
            WorkspaceSettingsStatus = "Workspace saved.";
            OnPropertyChanged(nameof(WorkspaceSettingsTitle));
        }
        catch (Exception exception)
        {
            WorkspaceSettingsError = $"Couldn't save workspace: {exception.Message}";
        }
        finally
        {
            IsSavingWorkspace = false;
        }
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

    private static IReadOnlyList<string> ReadStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static bool ReadBool(string? json, string key, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            return JsonNode.Parse(json) is JsonObject value
                && value[key] is JsonValue node
                && node.TryGetValue<bool>(out var result) ? result : fallback;
        }
        catch (JsonException) { return fallback; }
    }

    private static string SetBooleans(string? json, params (string Key, bool Value)[] values)
    {
        JsonObject result;
        try { result = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { result = new JsonObject(); }
        foreach (var (key, value) in values) result[key] = value;
        return result.ToJsonString();
    }

    partial void OnCurrentWorkspaceChanged(Workspace? value) => OnPropertyChanged(nameof(HeaderTitle));
    partial void OnCurrentThreadChanged(ThreadInfo? value) => OnPropertyChanged(nameof(HeaderTitle));
    partial void OnSelectedWorkspaceForSettingsChanged(Workspace? value) => OnPropertyChanged(nameof(WorkspaceSettingsTitle));

    partial void OnCurrentContextSectionChanged(MobileContextSection value)
    {
        OnPropertyChanged(nameof(IsThreadsContext));
        OnPropertyChanged(nameof(IsFilesContext));
        OnPropertyChanged(nameof(IsWorkspacesContext));
        OnPropertyChanged(nameof(IsSettingsContext));
        OnPropertyChanged(nameof(IsAccountContext));
    }

    partial void OnCurrentMainContentChanged(MobileMainContent value)
    {
        OnPropertyChanged(nameof(IsChatOpen));
        OnPropertyChanged(nameof(IsWorkspaceSettingsOpen));
        OnPropertyChanged(nameof(IsFilesOpen));
        OnPropertyChanged(nameof(IsGeneralSettingsOpen));
        OnPropertyChanged(nameof(IsModelsSettingsOpen));
        OnPropertyChanged(nameof(IsToolsSettingsOpen));
        OnPropertyChanged(nameof(IsSkillsSettingsOpen));
        OnPropertyChanged(nameof(IsAboutSettingsOpen));
        OnPropertyChanged(nameof(IsAccountOpen));
    }
}
