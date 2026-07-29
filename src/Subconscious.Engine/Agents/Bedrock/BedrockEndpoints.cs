namespace Subconscious.Engine.Agents.Bedrock;

/// <summary>
/// URL construction for the Amazon Bedrock Runtime Converse API.
/// Mirrors the region/endpoint resolution in <c>agent.py</c>'s <c>_bedrock_region</c> /
/// <c>_build_bedrock_model</c>.
/// </summary>
public static class BedrockEndpoints
{
    /// <summary>Default region used when none is configured and none can be inferred.</summary>
    public const string DefaultRegion = "us-east-1";

    /// <summary>
    /// Resolve the AWS region for a Bedrock model, in the same precedence order as
    /// <c>agent.py</c>'s <c>_bedrock_region</c>:
    /// <list type="number">
    /// <item>explicit <see cref="ModelConfig.Region"/></item>
    /// <item><see cref="ModelConfig.BaseUrl"/> (some users store the region there)</item>
    /// <item>region embedded in a foundation-model / inference-profile ARN</item>
    /// <item><c>AWS_REGION</c> / <c>AWS_DEFAULT_REGION</c> environment variables</item>
    /// </list>
    /// Returns null when no region can be determined (the caller decides whether to default).
    /// </summary>
    public static string? ResolveRegion(ModelConfig config)
    {
        var region = Trim(config.Region) ?? Trim(config.BaseUrl);
        if (region is not null)
        {
            return region;
        }

        // arn:aws:bedrock:<region>:<account>:...
        var model = Trim(config.Model);
        if (model is not null && model.StartsWith("arn:aws:bedrock:", StringComparison.Ordinal))
        {
            var parts = model.Split(':');
            if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                return parts[3];
            }
        }

        return Trim(Environment.GetEnvironmentVariable("AWS_REGION"))
            ?? Trim(Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION"));
    }

    /// <summary>Base service endpoint for a region, e.g. <c>https://bedrock-runtime.us-east-1.amazonaws.com</c>.</summary>
    public static string ServiceEndpoint(string region) => $"https://bedrock-runtime.{region}.amazonaws.com";

    /// <summary>
    /// Full Converse URL for a model. <paramref name="streaming"/> selects
    /// <c>converse-stream</c> (binary event-stream framed) over <c>converse</c>.
    /// The model id is URL-escaped so inference-profile ARNs (which contain <c>:</c> and
    /// <c>/</c>) are transmitted correctly.
    /// </summary>
    public static string ConverseUrl(string region, string modelId, bool streaming)
    {
        var action = streaming ? "converse-stream" : "converse";
        return $"{ServiceEndpoint(region)}/model/{Uri.EscapeDataString(modelId)}/{action}";
    }

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
