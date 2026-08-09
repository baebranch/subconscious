using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Persistent settings page for configured tools in the engine registry.</summary>
public sealed partial class ToolSettingsFormViewModel : ViewModelBase
{
    private readonly EngineClient _client = new();
    public ObservableCollection<ToolRegistryViewModel> Tools { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorText;

    [RelayCommand] private Task Load() => LoadAsync();

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorText = null;
        try
        {
            await EnsureConnectedAsync();
            var tools = await _client.ListToolRegistryAsync();
            Tools.Clear();
            foreach (var tool in tools) Tools.Add(new ToolRegistryViewModel(this, tool));
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void AddTool()
    {
        ErrorText = null;
        Tools.Add(new ToolRegistryViewModel(this) { IsExpanded = true });
    }

    internal void Remove(ToolRegistryViewModel tool) => Tools.Remove(tool);
    internal async Task<ToolRegistry> CreateAsync(UpsertToolRegistryRequest request)
    {
        await EnsureConnectedAsync();
        return await _client.CreateToolRegistryAsync(request);
    }

    internal async Task<ToolRegistry> UpdateAsync(string uuid, UpsertToolRegistryRequest request)
    {
        await EnsureConnectedAsync();
        return await _client.UpdateToolRegistryAsync(uuid, request);
    }

    internal async Task<bool> DeleteAsync(string uuid)
    {
        await EnsureConnectedAsync();
        return await _client.DeleteToolRegistryAsync(uuid);
    }

    private async Task EnsureConnectedAsync()
    {
        if (!_client.IsRestConnected)
        {
            await _client.ConnectRestAsync(MauiProgram.DevMode);
        }
    }
}

/// <summary>Card state for one registry entity. Name remains server-owned and is intentionally not editable.</summary>
public sealed partial class ToolRegistryViewModel : ViewModelBase
{
    private readonly ToolSettingsFormViewModel _owner;
    private string? _uuid;
    private string? _description;
    private string? _authEnvVar;
    private string? _status;

    public ToolRegistryViewModel(ToolSettingsFormViewModel owner) => _owner = owner;
    public ToolRegistryViewModel(ToolSettingsFormViewModel owner, ToolRegistry tool) : this(owner) => Apply(tool);

    public IReadOnlyList<string> ToolTypeOptions { get; } = ["script", "mcp", "api"];
    public IReadOnlyList<string> ScriptLanguageOptions { get; } = ["python", "javascript", "typescript"];
    public IReadOnlyList<string> AuthTypeOptions { get; } = ["", "api_key", "oauth"];
    public bool IsExisting => _uuid is not null;
    public bool IsScriptTool => ToolType == "script";
    public bool IsEndpointTool => ToolType is "mcp" or "api";
    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? "New tool" : Alias;
    public string Summary => string.Join(" · ", new[] { ToolType, IsScriptTool ? ScriptPath : EndpointUrl }.Where(value => !string.IsNullOrWhiteSpace(value)));

    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private string _toolType = "script";
    [ObservableProperty] private string _scriptPath = string.Empty;
    [ObservableProperty] private string _scriptLanguage = "python";
    [ObservableProperty] private string _endpointUrl = string.Empty;
    [ObservableProperty] private string _authType = string.Empty;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _errorText;

    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = null;
        if (string.IsNullOrWhiteSpace(Alias))
        {
            ErrorText = "Alias is required.";
            return;
        }
        if (IsScriptTool && string.IsNullOrWhiteSpace(ScriptPath))
        {
            ErrorText = "A script path is required for a script tool.";
            return;
        }
        if (IsEndpointTool && string.IsNullOrWhiteSpace(EndpointUrl))
        {
            ErrorText = "An endpoint URL is required for this tool type.";
            return;
        }

        IsSaving = true;
        try
        {
            var request = new UpsertToolRegistryRequest
            {
                Alias = Alias.Trim(),
                ToolType = ToolType,
                ScriptPath = IsScriptTool ? NullIfEmpty(ScriptPath) : null,
                ScriptLanguage = IsScriptTool ? NullIfEmpty(ScriptLanguage) : null,
                EndpointUrl = IsEndpointTool ? NullIfEmpty(EndpointUrl) : null,
                AuthType = NullIfEmpty(AuthType),
                Description = _description,
                AuthEnvVar = _authEnvVar,
                Status = _status,
            };
            Apply(_uuid is null ? await _owner.CreateAsync(request) : await _owner.UpdateAsync(_uuid, request));
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        ErrorText = null;
        if (_uuid is null)
        {
            _owner.Remove(this);
            return;
        }
        IsSaving = true;
        try
        {
            if (await _owner.DeleteAsync(_uuid)) _owner.Remove(this);
            else ErrorText = "This tool no longer exists.";
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void Apply(ToolRegistry tool)
    {
        _uuid = tool.Uuid;
        _description = tool.Description;
        _authEnvVar = tool.AuthEnvVar;
        _status = tool.Status;
        Alias = tool.Alias ?? tool.Name;
        ToolType = tool.ToolType;
        ScriptPath = tool.ScriptPath ?? string.Empty;
        ScriptLanguage = tool.ScriptLanguage ?? "python";
        EndpointUrl = tool.EndpointUrl ?? string.Empty;
        AuthType = tool.AuthType ?? string.Empty;
        OnPropertyChanged(nameof(IsExisting));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    partial void OnAliasChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnToolTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsScriptTool));
        OnPropertyChanged(nameof(IsEndpointTool));
        OnPropertyChanged(nameof(Summary));
    }
    partial void OnScriptPathChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnEndpointUrlChanged(string value) => OnPropertyChanged(nameof(Summary));
}
