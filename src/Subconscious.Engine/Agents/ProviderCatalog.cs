using LlmTornado.Code;

namespace Subconscious.Engine.Agents;

/// <summary>
/// Maps the provider display names stored in <see cref="ModelConfig.Provider"/> (as typed by
/// the user — "OpenAI", "Ollama", "LM Studio", etc.) to LLM Tornado's <see cref="LLmProviders"/>
/// enum. Mirrors <c>agent.py</c>'s <c>_PROVIDER_MAP</c> / <c>_provider_prefix</c>.
///
/// <para>
/// <b>Bedrock:</b> LLM Tornado has no native AWS Bedrock connector (its own
/// <c>FeatureMatrix.md</c> does not list Bedrock, and Tornado's "Custom" provider only covers
/// OpenAI-compatible APIs — Bedrock's Converse API is not). Subconscious therefore implements
/// Bedrock itself (<c>Bedrock/BedrockChatClient.cs</c>), plugged into the same
/// <see cref="Microsoft.Extensions.AI.IChatClient"/> seam as every Tornado-backed provider. It
/// is fully supported; it just isn't reachable through <see cref="Resolve"/>, so callers use
/// <see cref="IsSelfImplemented"/> to route it (which <see cref="AgentManager"/> does).
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

    // Providers Subconscious implements itself rather than routing through a Tornado connector.
    // Bedrock has no native Tornado connector, so Subconscious ships its own Converse-API
    // provider (see Bedrock/BedrockChatClient.cs) — it is fully supported, just not via
    // LLmProviders, hence excluded from Resolve().
    private static readonly HashSet<string> SelfImplementedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bedrock",
    };

    // Providers with no connector at all today. "hugging face" has no Tornado connector
    // (agent.py routed it through the openai-compatible path with a provider-specific env var,
    // but there's no equivalent HF Inference Endpoints shape in Tornado's Custom provider).
    private static readonly HashSet<string> UnsupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "hugging face",
    };

    /// <summary>
    /// True when Subconscious can build a chat client for this provider — either via a Tornado
    /// connector (<see cref="Resolve"/>) or via a Subconscious-implemented provider such as
    /// Bedrock (<see cref="IsSelfImplemented"/>).
    /// </summary>
    public static bool IsSupported(string providerName) =>
        !UnsupportedProviders.Contains(providerName)
        && (SelfImplementedProviders.Contains(providerName)
            || DirectProviders.ContainsKey(providerName)
            || OpenAiCompatibleProviders.Contains(providerName));

    /// <summary>
    /// True when this provider is implemented by Subconscious directly rather than routed through
    /// a Tornado connector — currently Bedrock only. <see cref="Resolve"/> does not apply to these.
    /// </summary>
    public static bool IsSelfImplemented(string providerName) => SelfImplementedProviders.Contains(providerName);

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
        if (SelfImplementedProviders.Contains(providerName))
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' is implemented by Subconscious directly, not via an LLM " +
                "Tornado connector — check IsSelfImplemented() before calling Resolve(). " +
                "AgentManager.BuildChatClient handles this routing.");
        }
        if (UnsupportedProviders.Contains(providerName))
        {
            throw new NotSupportedException(
                $"Provider '{providerName}' has no connector yet. See translation.md §4.4/§9 for the " +
                "tracked decision on how to add it (Hugging Face: no equivalent OpenAI-compatible " +
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
