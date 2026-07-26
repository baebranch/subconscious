namespace Subconscious.Engine.Agents;

/// <summary>
/// A configured model, as stored in the engine's secrets (once Phase 1's secrets store
/// lands). Mirrors the Python model-config dict in <c>secrets["models"][id]</c>, consumed
/// by <c>agent.py</c>'s <c>build_agent</c>/<c>set_env_for_model</c>.
/// </summary>
/// <param name="Id">Stable identifier for this configured model (a dictionary key in Python).</param>
/// <param name="Provider">
/// Provider display name as stored/typed by the user (e.g. "OpenAI", "Ollama", "Bedrock") —
/// resolved to a Tornado <see cref="LlmTornado.Code.LLmProviders"/> via <see cref="ProviderCatalog"/>.
/// </param>
/// <param name="Model">Raw model name/id, or an already-qualified "provider:model" string.</param>
/// <param name="ApiKey">API key/bearer token for the provider, if any.</param>
/// <param name="SystemPrompt">Default system prompt for agents built from this config.</param>
/// <param name="BaseUrl">
/// Custom base URL, for OpenAI-compatible/local providers (Ollama, LM Studio, custom
/// endpoints) or as a Bedrock region override (mirrors <c>agent.py</c>'s dual use of this
/// field).
/// </param>
/// <param name="Region">Explicit AWS region for Bedrock, if set.</param>
/// <param name="AwsAccessKeyId">Explicit AWS access key id for Bedrock, if set.</param>
/// <param name="AwsSecretAccessKey">Explicit AWS secret access key for Bedrock, if set.</param>
/// <param name="AwsSessionToken">Explicit AWS session token for Bedrock, if set.</param>
/// <param name="ContextWindow">Model context window (tokens); falls back to a conservative default.</param>
/// <param name="StreamTimeoutSeconds">Inactivity timeout (seconds) for streaming; falls back to a default.</param>
public sealed record ModelConfig(
    string Id,
    string Provider,
    string Model,
    string? ApiKey = null,
    string? SystemPrompt = null,
    string? BaseUrl = null,
    string? Region = null,
    string? AwsAccessKeyId = null,
    string? AwsSecretAccessKey = null,
    string? AwsSessionToken = null,
    int? ContextWindow = null,
    double? StreamTimeoutSeconds = null);
