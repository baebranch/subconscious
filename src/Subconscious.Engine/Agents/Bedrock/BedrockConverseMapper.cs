using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Subconscious.Engine.Agents.Bedrock;

/// <summary>
/// Translates between <c>Microsoft.Extensions.AI</c> chat types and Amazon Bedrock's
/// <c>Converse</c> / <c>ConverseStream</c> JSON shapes.
///
/// <para>
/// Bedrock's Converse API is deliberately model-agnostic (one schema across Anthropic, Llama,
/// Mistral, Titan, ...), which is what makes a single mapper viable — the same reason
/// <c>agent.py</c> could use pydantic-ai's <c>BedrockConverseModel</c> for every Bedrock model.
/// </para>
///
/// <para>
/// Notable shape differences from OpenAI-style APIs, handled here:
/// the system prompt is a top-level <c>system</c> array rather than a message with
/// <c>role: "system"</c>; Bedrock accepts only <c>user</c>/<c>assistant</c> roles; and
/// message content is always an array of typed blocks.
/// </para>
/// </summary>
public static class BedrockConverseMapper
{
    /// <summary>
    /// Build a Converse request body from MEAI messages and options.
    /// System messages are hoisted into the top-level <c>system</c> field.
    /// </summary>
    public static JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var systemBlocks = new JsonArray();
        var messageArray = new JsonArray();

        foreach (var message in messages)
        {
            var text = message.Text;

            if (message.Role == ChatRole.System)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    systemBlocks.Add(new JsonObject { ["text"] = text });
                }
                continue;
            }

            // Bedrock only accepts "user" and "assistant"; anything else (e.g. tool output
            // rendered as text) is attributed to the user turn so no content is silently lost.
            var role = message.Role == ChatRole.Assistant ? "assistant" : "user";

            messageArray.Add(new JsonObject
            {
                ["role"] = role,
                ["content"] = new JsonArray { new JsonObject { ["text"] = text ?? string.Empty } },
            });
        }

        var request = new JsonObject { ["messages"] = messageArray };

        if (systemBlocks.Count > 0)
        {
            request["system"] = systemBlocks;
        }

        var inferenceConfig = BuildInferenceConfig(options);
        if (inferenceConfig is not null)
        {
            request["inferenceConfig"] = inferenceConfig;
        }

        return request;
    }

    private static JsonObject? BuildInferenceConfig(ChatOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var config = new JsonObject();
        if (options.MaxOutputTokens is { } maxTokens)
        {
            config["maxTokens"] = maxTokens;
        }
        if (options.Temperature is { } temperature)
        {
            config["temperature"] = temperature;
        }
        if (options.TopP is { } topP)
        {
            config["topP"] = topP;
        }
        if (options.StopSequences is { Count: > 0 } stops)
        {
            config["stopSequences"] = new JsonArray([.. stops.Select(s => (JsonNode)s!)]);
        }

        return config.Count > 0 ? config : null;
    }

    /// <summary>
    /// Extract the assistant text from a non-streaming Converse response body, concatenating all
    /// <c>text</c> content blocks.
    /// </summary>
    public static string ExtractResponseText(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("output", out var output)
            || !output.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Extract the stop reason from a non-streaming Converse response, or null when absent.
    /// </summary>
    public static string? ExtractStopReason(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement.TryGetProperty("stopReason", out var stopReason)
               && stopReason.ValueKind == JsonValueKind.String
            ? stopReason.GetString()
            : null;
    }

    /// <summary>
    /// Extract the incremental text from a single <c>contentBlockDelta</c> streaming frame
    /// payload. Returns null for frames that carry no text delta (e.g. tool-use deltas), so
    /// callers can skip them without special-casing every event type.
    /// </summary>
    public static string? ExtractDeltaText(string framePayloadJson)
    {
        using var document = JsonDocument.Parse(framePayloadJson);
        return document.RootElement.TryGetProperty("delta", out var delta)
               && delta.TryGetProperty("text", out var text)
               && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
    }

    /// <summary>
    /// Extract token usage from a Converse response or a streaming <c>metadata</c> frame.
    /// Returns null when no usage block is present.
    /// </summary>
    public static UsageDetails? ExtractUsage(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("usage", out var usage))
        {
            return null;
        }

        var details = new UsageDetails();
        if (usage.TryGetProperty("inputTokens", out var input) && input.TryGetInt64(out var inputTokens))
        {
            details.InputTokenCount = inputTokens;
        }
        if (usage.TryGetProperty("outputTokens", out var output) && output.TryGetInt64(out var outputTokens))
        {
            details.OutputTokenCount = outputTokens;
        }
        if (usage.TryGetProperty("totalTokens", out var total) && total.TryGetInt64(out var totalTokens))
        {
            details.TotalTokenCount = totalTokens;
        }
        return details;
    }
}
