using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;
using Subconscious.Desktop.Services;

namespace Subconscious.Desktop.ViewModels;


/// <summary>The right-hand context panel's sub-header sections. Each shows a placeholder pane for
/// now — Threads is the default/left-most section.</summary>
public enum ContextPanelSection
{
    Threads,
    Files,
    Workspaces,
    Settings,
    Account,
}

/// <summary>Settings pages that mirror the desktop Python client's settings modes.</summary>
public enum SettingsPage
{
    General,
    Models,
    Tools,
    Skills,
    About,
}

/// <summary>
/// Root view model for the main page: the chat pane (the wired-up vertical slice), the center
/// utility panel, the right context panel, and the panel widths the user drags the dividers to.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    // Drag limits. The center panel additionally keeps a minimum share of the window — that cap
    // depends on the current window width, so MainPage applies it (see MainPage.ClampChatWidth).
    public const double MinChatPanelWidth = 280;
    public const double MaxChatPanelWidth = 720;
    public const double MinContextPanelWidth = 240;
    public const double MaxContextPanelWidth = 640;
    public const double DefaultChatPanelWidth = 380;
    public const double DefaultContextPanelWidth = 360;

    private readonly DesktopUiStateStore _desktopUiStateStore;
    private readonly PanelConfigurationStore _panelConfigurationStore;
    private readonly SidebarPositionStore _sidebarPositionStore;
    private readonly ThemeService _theme;

    private double _chatPanelWidth;
    private double _contextPanelWidth;
    private long _themeRevision;
    private int? _selectedWorkspaceId;
    private SettingsPage _lastSettingsPage = SettingsPage.General;
    private CancellationTokenSource? _desktopStateSaveDelay;
    private DesktopWindowPlacement? _windowPlacement;
    private bool _desktopStateInitialized;

    /// <summary>Prevents restoration and first-run initialization from overwriting stored state.</summary>
    private bool _restoring;

    /// <summary>Raised after the asynchronous engine-backed desktop state is available.</summary>
    public event EventHandler? DesktopStateRestored;

    /// <summary>The native window's last normal bounds and whether it was maximized on exit.</summary>
    public DesktopWindowPlacement? WindowPlacement => _windowPlacement;

    public ChatViewModel Chat { get; } = new();

    /// <summary>Local, workspace-allow-list-scoped file browsing and text editing state.</summary>
    public FileWorkspaceViewModel FileEditor { get; } = new();

    // Model and tool configurations are persisted by their respective engine APIs. Skills have no
    // matching API endpoint yet, so their cards remain editable session-only scaffolding.
    public ModelSettingsFormViewModel ModelSettingsForm { get; } = new();
    public ToolSettingsFormViewModel ToolSettingsForm { get; } = new();
    public SkillSettingsPageViewModel SkillSettingsPage { get; } = new();
    public AboutSettingsViewModel AboutSettings { get; } = new();

    /// <summary>Where the engine reads/writes its data (db, runtime.json, logs) — shown read-only
    /// on the Settings pane. Same directory <see cref="Engine.EngineDiscovery"/> probes to find a
    /// running engine, so what's displayed here is exactly where that lookup is looking.</summary>
    public string DataDirectory { get; } = Engine.EngineDiscovery.DataDirectory(MauiProgram.DevMode);

    /// <summary>Theme-aware icon color for nested MauiIcons markup. A direct DynamicResource does
    /// not resolve inside that markup extension, so this value is explicitly refreshed when the
    /// active palette changes.</summary>
    public Color IconColor => Application.Current?.Resources.TryGetValue("PrimaryTextColor", out var value) == true
        && value is Color color
        ? color
        : Colors.Black;

    /// <summary>A monotonically increasing input for collection-item MultiBindings. Its value is
    /// irrelevant to selection comparisons; the change notification forces converters to resolve
    /// newly replaced palette resources immediately.</summary>
    public long ThemeRevision => _themeRevision;

    /// <summary>The six persisted full arrangements available from General Settings.</summary>
    public IReadOnlyList<PanelConfigurationOption> PanelConfigurationOptions => PanelConfigurationCatalog.Options;

    [ObservableProperty]
    private PanelConfiguration _panelConfiguration = PanelConfiguration.ContextChatMain;

    /// <summary>The picker-facing representation of <see cref="PanelConfiguration"/>.</summary>
    public PanelConfigurationOption? SelectedPanelConfiguration
    {
        get => PanelConfigurationCatalog.OptionFor(PanelConfiguration);
        set
        {
            if (value is { } option)
            {
                PanelConfiguration = option.Value;
            }
        }
    }

    /// <summary>The two persisted edges available for the navigation rail.</summary>
    public IReadOnlyList<SidebarPositionOption> SidebarPositionOptions => SidebarPositionCatalog.Options;

    [ObservableProperty]
    private SidebarPosition _sidebarPosition = SidebarPosition.Left;

    /// <summary>The picker-facing representation of <see cref="SidebarPosition"/>.</summary>
    public SidebarPositionOption? SelectedSidebarPosition
    {
        get => SidebarPositionCatalog.OptionFor(SidebarPosition);
        set
        {
            if (value is { } option)
            {
                SidebarPosition = option.Value;
            }
        }
    }

    public MainViewModel(
        DesktopUiStateStore desktopUiStateStore,
        PanelConfigurationStore panelConfigurationStore,
        SidebarPositionStore sidebarPositionStore,
        ThemeService theme)
    {
        _desktopUiStateStore = desktopUiStateStore;
        _panelConfigurationStore = panelConfigurationStore;
        _sidebarPositionStore = sidebarPositionStore;
        _theme = theme;
        FileEditor.RefreshEditorTheme();
        _theme.Changed += OnThemeChanged;
        FileEditor.NavigationStateChanged += (_, _) => QueueDesktopStateSave();
        Chat.PropertyChanged += OnChatPropertyChanged;
        Chat.SelectionChanged += (_, _) =>
        {
            PersistDesktopStateImmediately();
            if (CurrentContextSection == ContextPanelSection.Files)
            {
                _ = FileEditor.LoadWorkspaceAsync(Chat.CurrentWorkspace);
            }
        };

        _chatPanelWidth = DefaultChatPanelWidth;
        _contextPanelWidth = DefaultContextPanelWidth;
    }

    private void OnChatPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentThread))
        {
            OnPropertyChanged(nameof(ActiveThreadUuid));
        }

        if (e.PropertyName == nameof(ChatViewModel.ComposerText))
        {
            QueueDesktopStateSave();
        }
    }

    // ── Panel sizing ──────────────────────────────────────────────────────────

    /// <summary>Width of the left chat panel, in device-independent units. Clamped on assignment
    /// so a fast drag can't push it past its limits.</summary>
    public double ChatPanelWidth
    {
        get => _chatPanelWidth;
        set => SetProperty(ref _chatPanelWidth, Math.Clamp(value, MinChatPanelWidth, MaxChatPanelWidth));
    }

    /// <summary>Width of the right context panel when it's open.</summary>
    public double ContextPanelWidth
    {
        get => _contextPanelWidth;
        set
        {
            if (SetProperty(ref _contextPanelWidth, Math.Clamp(value, MinContextPanelWidth, MaxContextPanelWidth)))
            {
                OnPropertyChanged(nameof(EffectiveContextPanelWidth));
            }
        }
    }

    /// <summary>The width the context column should actually get — zero while the panel is
    /// toggled off, so the center panel takes the space back.</summary>
    public double EffectiveContextPanelWidth => IsContextPanelOpen ? ContextPanelWidth : 0;

    /// <summary>Queues a coalesced write to the engine after a completed layout change.</summary>
    public void SaveLayout() => QueueDesktopStateSave();

    /// <summary>Records native window geometry after state restoration, avoiding startup defaults overwriting it.</summary>
    public void UpdateWindowPlacement(DesktopWindowPlacement placement)
    {
        if (!_desktopStateInitialized || _restoring || placement.Width <= 0 || placement.Height <= 0
            || _windowPlacement == placement)
        {
            return;
        }

        _windowPlacement = placement;
        QueueDesktopStateSave();
    }

    /// <summary>Requests a final immediate state save when the native window is closing.</summary>
    public void PersistDesktopStateOnExit()
    {
        if (_desktopStateInitialized)
        {
            PersistDesktopStateImmediately();
        }
    }

    /// <summary>Restores both dividers to their design defaults — bound to a double-click on
    /// either divider, the usual desktop convention.</summary>
    [RelayCommand]
    private void ResetPanelWidths()
    {
        ChatPanelWidth = DefaultChatPanelWidth;
        ContextPanelWidth = DefaultContextPanelWidth;
        SaveLayout();
    }

    /// <summary>Initializes chat from the saved selection, then restores the remaining Desktop UI state.</summary>
    public async Task InitializeEngineBackedStateAsync(bool dev)
    {
        DesktopUiState? state = null;
        _restoring = true;
        try
        {
            try
            {
                state = await _desktopUiStateStore.LoadAsync(dev);
            }
            catch (Exception)
            {
                // Chat remains usable with default state if the local settings API is unavailable.
            }

            await Chat.InitializeAsync(
                dev,
                state?.ActiveWorkspaceId,
                state?.SelectedThreadId,
                state?.ShowAllThreads ?? false,
                restoreSelection: state is not null);
            await LoadPanelConfigurationAsync(dev);
            await LoadSidebarPositionAsync(dev);

            if (state is not null)
            {
                var contextValue = string.IsNullOrWhiteSpace(state.CurrentContext)
                    ? state.CurrentView
                    : state.CurrentContext;
                if (Enum.TryParse<ContextPanelSection>(contextValue, ignoreCase: true, out var context)
                    && Enum.IsDefined(context))
                {
                    CurrentContextSection = context;
                }
                IsContextPanelOpen = state.ContextVisible;
                ChatPanelWidth = state.ChatPanelWidth;
                ContextPanelWidth = state.ContextPanelWidth;
                _windowPlacement = state.WindowPlacement;
                Chat.ComposerText = state.ChatboxText;
                _selectedWorkspaceId = state.SelectedWorkspaceId;
                if (Enum.TryParse<SettingsPage>(state.SelectedSetting, ignoreCase: true, out var setting)
                    && Enum.IsDefined(setting))
                {
                    _lastSettingsPage = setting;
                }

                await FileEditor.RestoreNavigationStateAsync(state.FileNavigation, Chat.Workspaces, Chat.CurrentWorkspace);
                OpenMainPanelForCurrentContext();
            }
        }
        finally
        {
            _restoring = false;
            _desktopStateInitialized = true;
            DesktopStateRestored?.Invoke(this, EventArgs.Empty);
        }

        // If a saved workspace/thread was deleted or unavailable, Chat selected a valid fallback.
        // Persist that outcome immediately so subsequent restarts do not repeat the failed restore.
        if (Chat.WorkspacesError is null)
        {
            PersistDesktopStateImmediately();
        }
    }

    private async Task LoadPanelConfigurationAsync(bool dev)
    {
        try
        {
            PanelConfiguration = await _panelConfigurationStore.LoadAsync(dev);
        }
        catch (Exception)
        {
            // A failed read leaves the default arrangement usable while the engine reconnects.
        }
    }

    private async Task LoadSidebarPositionAsync(bool dev)
    {
        try
        {
            SidebarPosition = await _sidebarPositionStore.LoadAsync(dev);
        }
        catch (Exception)
        {
            // A failed read leaves the default left-side rail usable while the engine reconnects.
        }
    }

    private DesktopUiState CreateDesktopUiState() => new()
    {
        CurrentView = CurrentContextSection.ToString().ToLowerInvariant(),
        CurrentContext = CurrentContextSection.ToString().ToLowerInvariant(),
        ContextVisible = IsContextPanelOpen,
        ChatPanelWidth = ChatPanelWidth,
        ContextPanelWidth = ContextPanelWidth,
        WindowPlacement = _windowPlacement,
        ActiveWorkspaceId = Chat.CurrentWorkspace?.Id,
        ShowAllThreads = Chat.CurrentWorkspace is null,
        SelectedThreadId = Chat.CurrentThread?.Id,
        SelectedWorkspaceId = _selectedWorkspaceId,
        SelectedSetting = _lastSettingsPage.ToString().ToLowerInvariant(),
        ChatboxText = Chat.ComposerText,
        FileNavigation = FileEditor.CaptureNavigationState(),
    };

    private void QueueDesktopStateSave() => ScheduleDesktopStateSave(TimeSpan.FromMilliseconds(300));

    /// <summary>Writes a completed workspace/thread selection without waiting for the composer debounce.</summary>
    private void PersistDesktopStateImmediately() => ScheduleDesktopStateSave(TimeSpan.Zero);

    private void ScheduleDesktopStateSave(TimeSpan delay)
    {
        if (_restoring)
        {
            return;
        }

        _desktopStateSaveDelay?.Cancel();
        var cancellation = _desktopStateSaveDelay = new CancellationTokenSource();
        _ = PersistDesktopStateAsync(delay, cancellation.Token);
    }

    private async Task PersistDesktopStateAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
            await _desktopUiStateStore.SaveAsync(CreateDesktopUiState(), MauiProgram.DevMode, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer UI state superseded this coalesced write.
        }
        catch (Exception)
        {
            // Local interaction remains responsive if the engine is transiently unavailable.
        }
    }

    partial void OnPanelConfigurationChanged(PanelConfiguration value)
    {
        OnPropertyChanged(nameof(SelectedPanelConfiguration));
        if (!_restoring)
        {
            _ = PersistPanelConfigurationAsync(value);
        }
    }

    partial void OnSidebarPositionChanged(SidebarPosition value)
    {
        OnPropertyChanged(nameof(SelectedSidebarPosition));
        if (!_restoring)
        {
            _ = PersistSidebarPositionAsync(value);
        }
    }

    private async Task PersistPanelConfigurationAsync(PanelConfiguration configuration)
    {
        try
        {
            await _panelConfigurationStore.SaveAsync(configuration, MauiProgram.DevMode);
        }
        catch (Exception)
        {
            // Applying the user's local selection is more important than surfacing a transient
            // engine connection error from a non-blocking settings Picker interaction.
        }
    }

    private async Task PersistSidebarPositionAsync(SidebarPosition position)
    {
        try
        {
            await _sidebarPositionStore.SaveAsync(position, MauiProgram.DevMode);
        }
        catch (Exception)
        {
            // Applying the user's local selection is more important than surfacing a transient
            // engine connection error from a non-blocking settings Picker interaction.
        }
    }

    // ── Context panel ─────────────────────────────────────────────────────────

    /// <summary>Whether the right-hand context panel is visible. Toggled from the header button.</summary>
    [ObservableProperty]
    private bool _isContextPanelOpen = true;

    /// <summary>Which sub-header section of the context panel is currently shown.</summary>
    [ObservableProperty]
    private ContextPanelSection _currentContextSection = ContextPanelSection.Threads;

    public bool IsThreadsSectionSelected => CurrentContextSection == ContextPanelSection.Threads;
    public bool IsFilesSectionSelected => CurrentContextSection == ContextPanelSection.Files;
    public bool IsWorkspacesSectionSelected => CurrentContextSection == ContextPanelSection.Workspaces;
    public bool IsSettingsSectionSelected => CurrentContextSection == ContextPanelSection.Settings;
    public bool IsAccountSectionSelected => CurrentContextSection == ContextPanelSection.Account;

    partial void OnIsContextPanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveContextPanelWidth));
        SaveLayout();
    }

    partial void OnCurrentContextSectionChanged(ContextPanelSection value)
    {
        OnPropertyChanged(nameof(IsThreadsSectionSelected));
        OnPropertyChanged(nameof(IsFilesSectionSelected));
        OnPropertyChanged(nameof(IsFileEditorOpen));
        OnPropertyChanged(nameof(IsCenterPanelIdle));
        OnPropertyChanged(nameof(IsWorkspacesSectionSelected));
        OnPropertyChanged(nameof(IsSettingsSectionSelected));
        OnPropertyChanged(nameof(IsAccountSectionSelected));
        SaveLayout();
    }

    /// <summary>Re-evaluates selection converters after ThemeService has replaced the runtime
    /// palette. Converters do not receive resource-change notifications on their own.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _themeRevision++;
        Chat.RefreshTheme();
        FileEditor.RefreshEditorTheme();
        OnPropertyChanged(nameof(ThemeRevision));
        OnPropertyChanged(nameof(IsContextPanelOpen));
        OnPropertyChanged(nameof(IsThreadsSectionSelected));
        OnPropertyChanged(nameof(IsFilesSectionSelected));
        OnPropertyChanged(nameof(IsFileEditorOpen));
        OnPropertyChanged(nameof(IsCenterPanelIdle));
        OnPropertyChanged(nameof(IsWorkspacesSectionSelected));
        OnPropertyChanged(nameof(IsSettingsSectionSelected));
        OnPropertyChanged(nameof(IsAccountSectionSelected));
        OnPropertyChanged(nameof(IsGeneralSettingsPageOpen));
        OnPropertyChanged(nameof(IsModelsSettingsPageOpen));
        OnPropertyChanged(nameof(IsToolsSettingsPageOpen));
        OnPropertyChanged(nameof(IsSkillsSettingsPageOpen));
        OnPropertyChanged(nameof(IsAboutSettingsPageOpen));
        OnPropertyChanged(nameof(ActiveWorkspaceUuid));
        OnPropertyChanged(nameof(ActiveThreadUuid));
        OnPropertyChanged(nameof(IconColor));
    }

    [RelayCommand]
    private void ToggleContextPanel() => IsContextPanelOpen = !IsContextPanelOpen;

    [RelayCommand]
    private void SelectContextSection(ContextPanelSection section) => OpenContextSection(section);

    /// <summary>Selects a Context Panel section from the persistent navigation rail, opens the
    /// panel, and restores that section's last main-panel destination.</summary>
    [RelayCommand]
    private void OpenContextSection(ContextPanelSection section)
    {
        CurrentContextSection = section;
        IsContextPanelOpen = true;
        OpenMainPanelForCurrentContext();
        PersistDesktopStateImmediately();
    }

    private void OpenMainPanelForCurrentContext()
    {
        switch (CurrentContextSection)
        {
            case ContextPanelSection.Files:
                CloseWorkspaceForm();
                CloseSettingsPage();
                _ = FileEditor.LoadWorkspaceAsync(Chat.CurrentWorkspace);
                break;
            case ContextPanelSection.Workspaces:
                OpenSelectedWorkspace();
                break;
            case ContextPanelSection.Settings:
                OpenSettingsPage(_lastSettingsPage);
                break;
            default:
                CloseWorkspaceForm();
                CloseSettingsPage();
                break;
        }
    }

    private void OpenSelectedWorkspace()
    {
        var workspace = Chat.Workspaces.FirstOrDefault(candidate => candidate.Id == _selectedWorkspaceId);
        if (workspace is null)
        {
            CloseWorkspaceForm();
            CloseSettingsPage();
            return;
        }

        OpenWorkspaceForm(new WorkspaceFormViewModel(Chat, workspace));
    }

    // ── Center panel: workspace form ──────────────────────────────────────────

    /// <summary>The center panel's workspace create/edit form. Null means the center panel shows
    /// its normal placeholder content instead.</summary>
    [ObservableProperty]
    private WorkspaceFormViewModel? _workspaceForm;

    public bool IsWorkspaceFormOpen => WorkspaceForm is not null;

    /// <summary>The workspace currently open in the center-panel management form. This is
    /// deliberately independent from <see cref="ChatViewModel.CurrentWorkspace"/>, which only
    /// determines the thread collection selected in the Threads subpanel.</summary>
    public string? ActiveWorkspaceUuid => WorkspaceForm?.Uuid;

    /// <summary>The thread currently loaded in the chat pane. Kept as a root property so selected
    /// row converters reevaluate after either a chat selection or a live palette change.</summary>
    public string? ActiveThreadUuid => Chat.CurrentThread?.Uuid;

    /// <summary>Whether the center panel is hosting the workspace-scoped text editor. File editing
    /// is mutually exclusive with every form page, including pages opened by a direct command.</summary>
    public bool IsFileEditorOpen => CurrentContextSection == ContextPanelSection.Files
        && WorkspaceForm is null
        && ActiveSettingsPage is null;

    /// <summary>True when the center panel isn't showing a workspace, settings, or file editor page.</summary>
    public bool IsCenterPanelIdle => WorkspaceForm is null && ActiveSettingsPage is null && !IsFileEditorOpen;

    partial void OnWorkspaceFormChanged(WorkspaceFormViewModel? value)
    {
        OnPropertyChanged(nameof(IsWorkspaceFormOpen));
        OnPropertyChanged(nameof(IsFileEditorOpen));
        OnPropertyChanged(nameof(ActiveWorkspaceUuid));
        OnPropertyChanged(nameof(IsCenterPanelIdle));
        QueueDesktopStateSave();
    }

    /// <summary>Opens the center panel's form for creating a new workspace — invoked by the
    /// Workspaces panel's "+" button instead of creating one directly.</summary>
    [RelayCommand]
    private void NewWorkspace() => OpenWorkspaceForm(new WorkspaceFormViewModel(Chat));

    /// <summary>Opens the center panel's form pre-filled with an existing workspace's details —
    /// invoked by selecting a workspace in the Workspaces panel list.</summary>
    [RelayCommand]
    private void EditWorkspace(Workspace workspace) => OpenWorkspaceForm(new WorkspaceFormViewModel(Chat, workspace));

    private void OpenWorkspaceForm(WorkspaceFormViewModel form, ToolPolicyEditorExpansionState? expansionState = null)
    {
        CloseSettingsPage();
        if (form.Id is > 0)
        {
            _selectedWorkspaceId = form.Id;
        }

        form.Saved += OnWorkspaceFormSaved;
        form.Cancelled += OnWorkspaceFormCancelled;
        WorkspaceForm = form;
        _ = form.InitializeAsync(expansionState);
        PersistDesktopStateImmediately();
    }

    private void OnWorkspaceFormSaved(object? sender, Workspace workspace)
    {
        _selectedWorkspaceId = workspace.Id;

        // The saved workspace needs a fresh immutable wire record, but policy-card expansion is
        // transient UI state and should survive the editor's post-save data reload.
        var expansionState = WorkspaceForm?.ToolPolicy.CaptureExpansionState();
        CloseWorkspaceForm();
        OpenWorkspaceForm(new WorkspaceFormViewModel(Chat, workspace), expansionState);
    }

    private void OnWorkspaceFormCancelled(object? sender, EventArgs e) => CloseWorkspaceForm();

    private void CloseWorkspaceForm()
    {
        if (WorkspaceForm is { } form)
        {
            form.Saved -= OnWorkspaceFormSaved;
            form.Cancelled -= OnWorkspaceFormCancelled;
        }
        WorkspaceForm = null;
    }

    // ── Center panel: settings pages ──────────────────────────────────────────

    /// <summary>The selected settings page. Null means no settings page currently occupies the
    /// center panel.</summary>
    [ObservableProperty]
    private SettingsPage? _activeSettingsPage;

    public bool IsGeneralSettingsPageOpen => ActiveSettingsPage == SettingsPage.General;
    public bool IsModelsSettingsPageOpen => ActiveSettingsPage == SettingsPage.Models;
    public bool IsToolsSettingsPageOpen => ActiveSettingsPage == SettingsPage.Tools;
    public bool IsSkillsSettingsPageOpen => ActiveSettingsPage == SettingsPage.Skills;
    public bool IsAboutSettingsPageOpen => ActiveSettingsPage == SettingsPage.About;

    partial void OnActiveSettingsPageChanged(SettingsPage? value)
    {
        if (value is { } page)
        {
            _lastSettingsPage = page;
        }
        OnPropertyChanged(nameof(IsGeneralSettingsPageOpen));
        OnPropertyChanged(nameof(IsModelsSettingsPageOpen));
        OnPropertyChanged(nameof(IsToolsSettingsPageOpen));
        OnPropertyChanged(nameof(IsSkillsSettingsPageOpen));
        OnPropertyChanged(nameof(IsAboutSettingsPageOpen));
        OnPropertyChanged(nameof(IsFileEditorOpen));
        OnPropertyChanged(nameof(IsCenterPanelIdle));
        QueueDesktopStateSave();
    }

    /// <summary>The backing form for the General settings page. The other pages are intentionally
    /// configuration empty states until matching desktop engine APIs are available.</summary>
    [ObservableProperty]
    private SettingsFormViewModel? _settingsForm;

    partial void OnSettingsFormChanged(SettingsFormViewModel? value)
    {
        OnPropertyChanged(nameof(IsCenterPanelIdle));
    }

    /// <summary>Opens a settings destination from the context panel and ensures the center panel
    /// hosts exactly one workspace or settings page at a time.</summary>
    [RelayCommand]
    private void OpenSettingsPage(SettingsPage page)
    {
        CloseWorkspaceForm();
        CloseSettingsForm();
        ActiveSettingsPage = page;

        if (page == SettingsPage.General)
        {
            var form = new SettingsFormViewModel(_theme, DataDirectory);
            form.Closed += OnSettingsFormClosed;
            SettingsForm = form;
        }
        else if (page == SettingsPage.Models)
        {
            _ = ModelSettingsForm.LoadAsync();
        }
        else if (page == SettingsPage.Tools)
        {
            _ = ToolSettingsForm.LoadAsync();
        }
    }

    private void OnSettingsFormClosed(object? sender, EventArgs e) => CloseSettingsPage();

    private void CloseSettingsPage()
    {
        CloseSettingsForm();
        ActiveSettingsPage = null;
    }

    private void CloseSettingsForm()
    {
        if (SettingsForm is { } form)
        {
            form.Closed -= OnSettingsFormClosed;
            form.Detach();
        }
        SettingsForm = null;
    }
}
