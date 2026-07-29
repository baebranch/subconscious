using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Agents.Bedrock;

namespace Subconscious.Engine.Agents;

/// <summary>
/// Builds <see cref="IChatClient"/> instances from stored <see cref="ModelConfig"/>s. Direct
/// analog of <c>agent.py</c>'s <c>AgentManager</c>: <c>build_agent</c>/<c>set_env_for_model</c>
/// resolved a provider + model string into a pydantic-ai <c>Agent</c>; here the same config
/// resolves into an LLM Tornado <see cref="TornadoApi"/> bridged to <see cref="IChatClient"/>
/// via <see cref="TornadoServiceExtensions.AsChatClient"/> (translation.md §4.4).
///
/// <para>
/// Unlike the Python version, no environment variable is set as a side effect
/// (<c>set_env_for_model</c> injected <c>OPENAI_API_KEY</c> etc. into the process
/// environment for pydantic-ai to pick up implicitly). Tornado takes credentials directly
/// via <see cref="ProviderAuthentication"/>/constructor arguments, so there is no such
/// global mutable state to manage — a straightforward simplification, not a behavior gap.
/// </para>
/// </summary>
public sealed class AgentManager
{
    /// <summary>
    /// Build an <see cref="IChatClient"/> for <paramref name="config"/>. This is the .NET
    /// entry point equivalent to <c>agent.py</c>'s <c>build_agent</c> for the interactive
    /// loop (tool-calling agents are composed on top of this in a later Phase 2 increment —
    /// see <c>ApprovalGate</c> for the HITL wrapper and translation.md Phase 2 notes).
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown for providers with no LLM Tornado connector yet (Bedrock, Hugging Face — see
    /// <see cref="ProviderCatalog"/>).
    /// </exception>
    public IChatClient BuildChatClient(ModelConfig config)
    {
        if (string.Equals(config.Provider, "subconscious", StringComparison.OrdinalIgnoreCase)
            && string.Equals(config.Model, "echo", StringComparison.OrdinalIgnoreCase))
        {
            return new EchoChatClient();
        }

        // Providers Subconscious implements itself (currently Bedrock, which has no Tornado
        // connector) still return an IChatClient, so callers can't tell the difference.
        if (ProviderCatalog.IsSelfImplemented(config.Provider.Trim()))
        {
            return new BedrockChatClient(config);
        }

        var api = BuildTornadoApi(config);
        var modelString = ResolveModelString(config);
        // No default ChatRequest is passed: the system prompt (agent.py's build_agent
        // system_prompt param) is composed into the ChatMessage list by the caller (the chat
        // orchestrator, later in Phase 2) as a system-role message, matching how
        // Microsoft.Extensions.AI expects system prompts to be supplied per-call rather than
        // baked into the client.
        return api.AsChatClient(modelString);
    }

    /// <summary>
    /// Construct the <see cref="TornadoApi"/> for <paramref name="config"/>'s provider,
    /// mirroring <c>agent.py</c>'s per-provider branching (custom base URL for
    /// Ollama/LM Studio/OpenAI-compatible endpoints; direct provider auth otherwise).
    /// </summary>
    private static TornadoApi BuildTornadoApi(ModelConfig config)
    {
        var provider = config.Provider.Trim();
        var apiKey = (config.ApiKey ?? string.Empty).Trim();

        if (ProviderCatalog.IsOpenAiCompatible(provider))
        {
            var baseUrl = (config.BaseUrl ?? ProviderCatalog.DefaultEndpoint(provider) ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new InvalidOperationException(
                    $"Model config '{config.Id}' uses provider '{provider}', which requires a base URL " +
                    "(no default endpoint is known for it).");
            }
            if (!baseUrl.EndsWith("/v1", StringComparison.Ordinal))
            {
                baseUrl += "/v1";
            }
            // Empty api key is fine for local providers (Ollama/LM Studio/custom) — Tornado's
            // Custom provider constructor accepts it as-is, matching agent.py's behavior of
            // not requiring OPENAI_API_KEY for these.
            return new TornadoApi(new Uri(baseUrl), apiKey, LLmProviders.Custom);
        }

        var resolvedProvider = ProviderCatalog.Resolve(provider);
        if (string.IsNullOrEmpty(apiKey) && !ProviderCatalog.RequiresNoApiKey(provider))
        {
            // Mirrors agent.py logging a warning rather than throwing when a key is missing —
            // the request will simply fail against the provider, which is the same outcome.
        }
        return new TornadoApi(resolvedProvider, apiKey);
    }

    /// <summary>
    /// Resolve the model string Tornado should use. An already-qualified "provider:model"
    /// string is passed through unchanged (mirrors <c>agent.py</c>: <c>":" in raw_model</c>);
    /// otherwise the raw model name is used as-is, since Tornado's <see cref="TornadoApi"/>
    /// is already scoped to the right provider via its constructor (unlike pydantic-ai, which
    /// resolves the provider from a "provider:model" string on a shared client).
    /// </summary>
    private static string ResolveModelString(ModelConfig config)
    {
        var raw = config.Model.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException($"Model config '{config.Id}' has an empty model name.");
        }
        return raw;
    }

}
