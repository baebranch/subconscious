using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Engine-backed workspace editor, including its raw JSON-backed policy settings.</summary>
public sealed partial class WorkspaceFormViewModel : ViewModelBase
{
    private readonly ChatViewModel _chat;
    private string? _rawApprovalConfig;
    private string? _rawRagConfig;

    public string? Uuid { get; }
    public int? Id { get; }
    public bool IsEditMode => Uuid is not null;
    public DateTime? CreatedAt { get; }
    public DateTime? UpdatedAt { get; }
    public ObservableCollection<ModelInfo> AvailableModels => _chat.AvailableModels;
    public ObservableCollection<string> WorkspacePaths { get; } = [];
    public ToolPolicyEditorViewModel ToolPolicy { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private ModelInfo? _selectedDefaultModel;
    [ObservableProperty] private string _newWorkspacePath = string.Empty;
    [ObservableProperty] private bool _buildKnowledgeGraph;
    [ObservableProperty] private bool _requireApprovalForQueries = true;
    [ObservableProperty] private bool _requireApprovalForMutations = true;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isInitializing;

    public event EventHandler<Workspace>? Saved;
    public event EventHandler? Cancelled;

    public WorkspaceFormViewModel(ChatViewModel chat)
    {
        _chat = chat;
        ToolPolicy.Changed += (_, _) => SaveCommand.NotifyCanExecuteChanged();
        SelectedDefaultModel = AvailableModels.FirstOrDefault();
    }

    public WorkspaceFormViewModel(ChatViewModel chat, Workspace workspace) : this(chat)
    {
        Uuid = workspace.Uuid;
        Id = workspace.Id;
        CreatedAt = workspace.CreatedAt;
        UpdatedAt = workspace.UpdatedAt;
        Name = workspace.Name;
        Description = workspace.Description;
        _rawApprovalConfig = workspace.ApprovalConfig;
        _rawRagConfig = workspace.RagConfig;
        foreach (var path in ReadDirectories(workspace.Directories))
        {
            AddStoredWorkspacePath(path);
        }
        BuildKnowledgeGraph = ReadBool(workspace.RagConfig, "semantic_graph", false);
        RequireApprovalForQueries = ReadBool(workspace.ApprovalConfig, "query", true);
        RequireApprovalForMutations = ReadBool(workspace.ApprovalConfig, "mutation", true);
        SelectedDefaultModel = AvailableModels.FirstOrDefault(model => model.Id == workspace.DefaultModelId)
            ?? AvailableModels.FirstOrDefault();
    }

    /// <summary>Loads the real catalog when the form opens; no static tool list is maintained by Desktop.</summary>
    public async Task InitializeAsync(ToolPolicyEditorExpansionState? expansionState = null)
    {
        if (IsInitializing)
        {
            return;
        }

        IsInitializing = true;
        ErrorText = null;
        try
        {
            var catalog = await _chat.GetToolCatalogAsync();
            var config = Uuid is null
                ? null
                : (await _chat.GetWorkspaceToolsConfigAsync(Uuid)).Config;
            ToolPolicy.Populate(catalog, config);
            ToolPolicy.RestoreExpansionState(expansionState);
        }
        catch (Exception exception)
        {
            ErrorText = $"Couldn't load tool policy: {exception.Message}";
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private bool CanSave => !IsSaving && !IsInitializing && Name.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            ErrorText = "Name is required.";
            return;
        }

        var directories = ParseDirectories();
        if (directories is null)
        {
            return;
        }

        IsSaving = true;
        ErrorText = null;
        try
        {
            var request = new CreateWorkspaceRequest
            {
                Name = name,
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                DefaultModelId = SelectedDefaultModel?.Id,
                ToolsConfig = ToolPolicy.SerializeDesiredConfig().ToJsonString(),
                Directories = JsonSerializer.Serialize(directories),
                ApprovalConfig = SetBoolean(_rawApprovalConfig, "query", RequireApprovalForQueries, "mutation", RequireApprovalForMutations),
                RagConfig = SetBoolean(_rawRagConfig, "semantic_graph", BuildKnowledgeGraph),
            };

            var workspace = IsEditMode
                ? await _chat.UpdateWorkspaceEntryAsync(Uuid!, request)
                : await _chat.CreateWorkspaceEntryAsync(request);
            Saved?.Invoke(this, workspace);
        }
        catch (Exception exception)
        {
            ErrorText = $"Failed to save: {exception.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    partial void OnNameChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsInitializingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void AddWorkspacePath()
    {
        var rawPath = NewWorkspacePath.Trim();
        if (rawPath.Length == 0)
        {
            ErrorText = "Enter an absolute folder path before adding it.";
            return;
        }

        if (!TryNormalizeWorkspacePath(rawPath, out var path))
        {
            ErrorText = $"Workspace paths must be fully-qualified absolute paths: {rawPath}";
            return;
        }
        if (WorkspacePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            ErrorText = "That workspace path is already allowed.";
            return;
        }

        WorkspacePaths.Add(path);
        NewWorkspacePath = string.Empty;
        ErrorText = null;
    }

    [RelayCommand]
    private void RemoveWorkspacePath(string? path)
    {
        if (path is not null)
        {
            WorkspacePaths.Remove(path);
        }
    }

    private List<string>? ParseDirectories()
    {
        var directories = new List<string>(WorkspacePaths.Count);
        foreach (var configuredPath in WorkspacePaths)
        {
            if (!TryNormalizeWorkspacePath(configuredPath, out var normalized))
            {
                ErrorText = $"Workspace paths must be fully-qualified absolute paths: {configuredPath}";
                return null;
            }
            if (!directories.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(normalized);
            }
        }
        return directories;
    }

    private void AddStoredWorkspacePath(string rawPath)
    {
        if (TryNormalizeWorkspacePath(rawPath, out var path)
            && !WorkspacePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            WorkspacePaths.Add(path);
        }
    }

    private static bool TryNormalizeWorkspacePath(string? rawPath, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath) || !Path.IsPathFullyQualified(rawPath.Trim()))
        {
            return false;
        }

        try
        {
            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawPath.Trim()));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ReadDirectories(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool ReadBool(string? raw, string key, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        try
        {
            var node = JsonNode.Parse(raw) as JsonObject;
            return node is not null && ToolPolicyEditorViewModel.ReadBool(node[key], fallback);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    /// <summary>Edits known boolean switches without discarding future/unknown policy fields.</summary>
    private static string SetBoolean(string? raw, string key, bool value, string? secondKey = null, bool secondValue = false)
    {
        JsonObject config;
        if (string.IsNullOrWhiteSpace(raw))
        {
            config = new JsonObject();
        }
        else
        {
            try
            {
                config = JsonNode.Parse(raw) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                config = new JsonObject();
            }
        }
        config[key] = value;
        if (secondKey is not null)
        {
            config[secondKey] = secondValue;
        }
        return config.ToJsonString();
    }
}
