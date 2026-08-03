using CommunityToolkit.Mvvm.ComponentModel;

namespace Subconscious.Desktop.ViewModels;

/// <summary>
/// Holds the desktop Model configuration form's input state. The engine currently exposes only
/// a read-only model catalogue, so this intentionally does not provide a save command.
/// </summary>
public sealed partial class ModelSettingsFormViewModel : ViewModelBase
{
    public IReadOnlyList<string> ProviderOptions { get; } =
    [
        "Anthropic", "Bedrock", "Cerebras", "Cohere", "Gemini", "Groq", "Hugging Face",
        "Mistral", "OpenAI", "OpenRouter", "xAI", "Alibaba Cloud Model Studio",
        "Azure AI Foundry", "DeepSeek", "Fireworks AI", "GitHub Models", "LiteLLM",
        "Nebius AI Studio", "Ollama", "Custom", "LM Studio", "Perplexity", "SambaNova",
        "Together AI",
    ];

    [ObservableProperty]
    private string _provider = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _contextWindow = string.Empty;

    public bool IsBaseUrlVisible => Provider is "Ollama" or "LM Studio" or "Custom";

    public string BaseUrlPlaceholder => Provider switch
    {
        "Ollama" => "http://localhost:11434/v1",
        "LM Studio" => "http://127.0.0.1:1234/v1",
        _ => string.Empty,
    };

    partial void OnProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsBaseUrlVisible));
        OnPropertyChanged(nameof(BaseUrlPlaceholder));
    }
}
