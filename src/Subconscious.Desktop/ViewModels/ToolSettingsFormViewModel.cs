using CommunityToolkit.Mvvm.ComponentModel;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Holds the configuration fields represented by the engine's tool_registry table.</summary>
public sealed partial class ToolSettingsFormViewModel : ViewModelBase
{
    public IReadOnlyList<string> ToolTypeOptions { get; } = ["script", "mcp", "api"];
    public IReadOnlyList<string> ScriptLanguageOptions { get; } = ["python", "javascript", "typescript"];
    public IReadOnlyList<string> AuthTypeOptions { get; } = ["", "api_key", "oauth"];
    public IReadOnlyList<string> StatusOptions { get; } = ["active", "disabled"];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _toolType = "script";

    [ObservableProperty]
    private string _scriptPath = string.Empty;

    [ObservableProperty]
    private string _scriptLanguage = "python";

    [ObservableProperty]
    private string _endpointUrl = string.Empty;

    [ObservableProperty]
    private string _authType = string.Empty;

    [ObservableProperty]
    private string _authEnvVar = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _status = "active";

    public bool IsScriptTool => ToolType == "script";
    public bool IsEndpointTool => ToolType is "mcp" or "api";
    public bool IsApiKeyAuthentication => AuthType == "api_key";
    public bool IsOAuthAuthentication => AuthType == "oauth";

    partial void OnToolTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsScriptTool));
        OnPropertyChanged(nameof(IsEndpointTool));
    }

    partial void OnAuthTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsApiKeyAuthentication));
        OnPropertyChanged(nameof(IsOAuthAuthentication));
    }
}
