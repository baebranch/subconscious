using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>API-backed list of encrypted model configurations exposed by the local engine.</summary>
public sealed partial class ModelSettingsFormViewModel : ViewModelBase
{
    private readonly EngineClient _client = new();

    public ObservableCollection<ModelConfigurationViewModel> Configurations { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorText;

    [RelayCommand]
    private Task Load() => LoadAsync();

    public async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorText = null;
        try
        {
            if (!_client.IsRestConnected)
            {
                await _client.ConnectRestAsync(MauiProgram.DevMode);
            }

            var configurations = await _client.ListModelConfigurationsAsync();
            Configurations.Clear();
            foreach (var configuration in configurations)
            {
                Configurations.Add(new ModelConfigurationViewModel(this, configuration));
            }
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
    private void AddConfiguration()
    {
        ErrorText = null;
        Configurations.Add(new ModelConfigurationViewModel(this) { IsExpanded = true });
    }

    internal void Remove(ModelConfigurationViewModel configuration) => Configurations.Remove(configuration);

    internal async Task<ModelConfiguration> CreateAsync(UpsertModelConfigurationRequest request)
    {
        await EnsureConnectedAsync();
        return await _client.CreateModelConfigurationAsync(request);
    }

    internal async Task<ModelConfiguration> UpdateAsync(string id, UpsertModelConfigurationRequest request)
    {
        await EnsureConnectedAsync();
        return await _client.UpdateModelConfigurationAsync(id, request);
    }

    internal async Task<bool> DeleteAsync(string id)
    {
        await EnsureConnectedAsync();
        return await _client.DeleteModelConfigurationAsync(id);
    }

    private async Task EnsureConnectedAsync()
    {
        if (!_client.IsRestConnected)
        {
            await _client.ConnectRestAsync(MauiProgram.DevMode);
        }
    }
}

/// <summary>Editable state for one encrypted model configuration.</summary>
public sealed partial class ModelConfigurationViewModel : ViewModelBase
{
    private readonly ModelSettingsFormViewModel _owner;
    private string? _id;

    public ModelConfigurationViewModel(ModelSettingsFormViewModel owner)
    {
        _owner = owner;
    }

    public ModelConfigurationViewModel(ModelSettingsFormViewModel owner, ModelConfiguration configuration)
        : this(owner)
    {
        Apply(configuration);
    }

    public IReadOnlyList<string> ProviderOptions { get; } =
    [
        "Anthropic", "Bedrock", "Cerebras", "Cohere", "Gemini", "Groq", "Hugging Face",
        "Mistral", "OpenAI", "OpenRouter", "xAI", "Alibaba Cloud Model Studio",
        "Azure AI Foundry", "DeepSeek", "Fireworks AI", "GitHub Models", "LiteLLM",
        "Nebius AI Studio", "Ollama", "Custom", "LM Studio", "Perplexity", "SambaNova",
        "Together AI",
    ];

    public bool IsExisting => _id is not null;

    [ObservableProperty]
    private string _provider = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    /// <summary>Write-only UI input; hydrated configurations never receive the stored secret.</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private bool _hasApiKey;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _contextWindow = string.Empty;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>The alias is the configuration's default, user-facing name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Alias)
        ? "New model configuration"
        : Alias;

    public string Summary => string.IsNullOrWhiteSpace(Provider) && string.IsNullOrWhiteSpace(Model)
        ? "Choose a provider and model"
        : string.Join(" · ", new[] { Provider, Model }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public bool IsBaseUrlVisible => Provider is "Ollama" or "LM Studio" or "Custom";

    public string BaseUrlPlaceholder => Provider switch
    {
        "Ollama" => "http://localhost:11434/v1",
        "LM Studio" => "http://127.0.0.1:1234/v1",
        _ => string.Empty,
    };

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorText = null;
        if (string.IsNullOrWhiteSpace(Provider) || string.IsNullOrWhiteSpace(Model))
        {
            ErrorText = "Provider and model are required.";
            return;
        }

        int? contextWindow = null;
        if (!string.IsNullOrWhiteSpace(ContextWindow))
        {
            if (!int.TryParse(ContextWindow, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedContextWindow)
                || parsedContextWindow <= 0)
            {
                ErrorText = "Context window must be a positive whole number.";
                return;
            }
            contextWindow = parsedContextWindow;
        }

        IsSaving = true;
        try
        {
            var request = new UpsertModelConfigurationRequest
            {
                Provider = Provider,
                Model = Model,
                Alias = Alias,
                BaseUrl = BaseUrl,
                ContextWindow = contextWindow,
                // A blank input on an existing row means "leave the encrypted key untouched".
                ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            };

            var saved = _id is null
                ? await _owner.CreateAsync(request)
                : await _owner.UpdateAsync(_id, request);
            Apply(saved);
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
        if (_id is null)
        {
            _owner.Remove(this);
            return;
        }

        IsSaving = true;
        try
        {
            if (await _owner.DeleteAsync(_id))
            {
                _owner.Remove(this);
            }
            else
            {
                ErrorText = "This model configuration no longer exists.";
            }
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

    private void Apply(ModelConfiguration configuration)
    {
        _id = configuration.Id;
        Provider = configuration.Provider;
        Model = configuration.Model;
        Alias = configuration.Alias ?? string.Empty;
        BaseUrl = configuration.BaseUrl ?? string.Empty;
        ContextWindow = configuration.ContextWindow?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        HasApiKey = configuration.HasApiKey;
        ApiKey = string.Empty;
        OnPropertyChanged(nameof(IsExisting));
    }

    partial void OnProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsBaseUrlVisible));
        OnPropertyChanged(nameof(BaseUrlPlaceholder));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnModelChanged(string value) => OnPropertyChanged(nameof(Summary));

    partial void OnAliasChanged(string value) => OnPropertyChanged(nameof(DisplayName));
}
