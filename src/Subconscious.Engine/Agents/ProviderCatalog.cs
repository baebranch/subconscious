using LlmTornado.Code;

namespace Subconscious.Engine.Agents;

/// <summary>
/// Maps the provider display names stored in <see cref="ModelConfig.Provider"/> (as typed by
/// the user — "OpenAI", "Ollama", "LM Studio", etc.) to LLM Tornado's <see cref="LLmProviders"/>
/// enum. Mirrors <c>agent.py</c>'s <c>_PROVIDER_MAP</c> / <c>_provider_prefix</c>.
///
/// <para>
/// <b>Known gap:</b> LLM Tornado has no native AWS Bedrock connector as of this writing (its own
/// <c>FeatureMatrix.md</c> does not list Bedrock, and Tornado's "Custom" provider only covers
/// OpenAI-compatible APIs — Bedrock's Converse API is not). <c>agent.py</c>'s Bedrock support
/// (<c>_build_bedrock_model</c>, region/credential resolution) therefore has no direct 1:1 port
/// yet. <see cref="Resolve"/> throws <see cref="NotSupportedException"/> for "bedrock" rather
/// than silently mis-routing it — see translation.md §4.4/§9 for the tracked decision (either
/// wait for/contribute a Tornado Bedrock connector, or bridge the AWS SDK for .NET's Bedrock
/// Runtime client through a hand-written <see cref="Microsoft.Extensions.AI.IChatClient"/> for
/// just this one provider).
/// </para>
/// </summary>
public static class ProviderCatalog
{
    // No API key needed for these: local/self-hosted, or credentials supplied out-of-band.
    private static readonly HashSet<string> NoApiKeyProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "ollama", "lm studio", "custom",
    };

    // Providers reached through Tornado's OpenAI-compatible "Custom" endpoint machinery
    // (agent.py's OpenAiChatModel + custom base_url path).
    private static readonly HashSet<string> OpenAiCompatibleProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "ollama", "lm studio", "custom",
        "azure ai foundry", "fireworks ai", "github models", "litellm",
        "nebius ai studio", "sambanova", "together ai",
        "alibaba cloud model studio",
    };

    private static readonly Dictionary<string, LLmProviders> DirectProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = LLmProviders.OpenAi,
        ["anthropic"] = LLmProviders.Anthropic,
        ["gemini"] = LLmProviders.Google,
        ["google"] = LLmProviders.Google,
        ["groq"] = LLmProviders.Groq,
        ["mistral"] = LLmProviders.Mistral,
        ["xai"] = LLmProviders.XAi,
        ["cohere"] = LLmProviders.Cohere,
        ["deepseek"] = LLmProviders.DeepSeek,
        ["openrouter"] = LLmProviders.OpenRouter,
        ["perplexity"] = LLmProviders.Perplexity,
        ["deepinfra"] = LLmProviders.DeepInfra,
        ["moonshotai"] = LLmProviders.MoonshotAi,
        ["alibaba"] = LLmProviders.Alibaba,
        ["requesty"] = LLmProviders.Requesty,
        ["upstage"] = LLmProviders.Upstage,
        ["minimax"] = LLmProviders.MiniMax,
        ["blablador"] = LLmProviders.Blablador,
        ["azure"] = LLmProviders.AzureOpenAi,
    };

    // Providers with no first-party Tornado connector today. "hugging face" has no Tornado
    // connector either (agent.py routed it through the openai-compatible path with a
    // provider-specific env var, but there's no equivalent HF Inference Endpoints shape in
    // Tornado's Custom provider) — tracked alongside Bedrock rather than guessed at.
    private static readonly HashSet<string> UnsupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bedrock", "hugging face",
    };

    /// <summary>True when <see cref="Resolve"/> would succeed for this provider name.</summary>
    public static bool IsSupported(string providerName) =>
        !UnsupportedProviders.Contains(providerName)
        && (DirectProviders.ContainsKey(providerName) || OpenAiCompatibleProviders.Contains(providerName));

    /// <summary>True when this provider needs no API key (local/self-hosted).</summary>
    public static bool RequiresNoApiKey(string providerName) => NoApiKeyProviders.Contains(providerName);

    /// <summary>True when this provider is reached via Tornado's OpenAI-compatible custom endpoint.</summary>
    public static bool IsOpenAiCompatible(string providerName) => OpenAiCompatibleProviders.Contains(providerName);

    /// <summary>
    /// Resolve a provider display name to a Tornado <see cref="LLmProviders"/> value.
    /// Throws <see cref="NotSupportedException"/> for Bedrock/Hugging Face (see class remarks)
    /// and <see cref="ArgumentException"/> for anything else unrecognized.
    /// </summary>
    public static LLmProviders Resolve(string providerName)
    {
        if (UnsupportedProviders.Contains(providerName))
        {
            throw new NotSupportedException(
                $"Provider '{providerName}' has no LLM Tornado connector yet. See translation.md " +
                "§4.4/§9 for the tracked decision on how to add it (Bedrock: no native Tornado " +
                "connector as of this writing; Hugging Face: no equivalent OpenAI-compatible " +
                "endpoint shape in Tornado's Custom provider).");
        }
        if (DirectProviders.TryGetValue(providerName, out var provider))
        {
            return provider;
        }
        if (OpenAiCompatibleProviders.Contains(providerName))
        {
            return LLmProviders.Custom;
        }
        throw new ArgumentException($"Unknown provider '{providerName}'.", nameof(providerName));
    }

    /// <summary>
    /// Default local endpoint for known self-hosted providers, mirroring
    /// <c>agent.py</c>'s <c>custom_endpoints()</c>. Returns null when there's no sensible
    /// default (the caller must supply <see cref="ModelConfig.BaseUrl"/>).
    /// </summary>
    public static string? DefaultEndpoint(string providerName) => providerName.ToLowerInvariant() switch
    {
        "ollama" => "http://localhost:11434/v1",
        "lm studio" => "http://127.0.0.1:1234/v1",
        _ => null,
    };
}
