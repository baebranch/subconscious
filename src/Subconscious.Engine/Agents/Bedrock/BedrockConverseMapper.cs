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
    /// System messages are hoisted into the top-level <c>system</c> field; function calls and
    /// results are preserved as Bedrock <c>toolUse</c>/<c>toolResult</c> content blocks.
    /// </summary>
    public static JsonObject BuildRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var systemBlocks = new JsonArray();
        var messageArray = new JsonArray();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (!string.IsNullOrEmpty(message.Text))
                {
                    systemBlocks.Add(new JsonObject { ["text"] = message.Text });
                }
                continue;
            }

            var content = BuildContent(message);
            if (content.Count == 0)
            {
                content.Add(new JsonObject { ["text"] = message.Text ?? string.Empty });
            }

            messageArray.Add(new JsonObject
            {
                // Bedrock only accepts user and assistant messages. Function results are user
                // content because they answer the preceding assistant tool-use request.
                ["role"] = message.Role == ChatRole.Assistant ? "assistant" : "user",
                ["content"] = content,
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

        var toolConfig = BuildToolConfig(options);
        if (toolConfig is not null)
        {
            request["toolConfig"] = toolConfig;
        }

        return request;
    }

    private static JsonArray BuildContent(ChatMessage message)
    {
        var content = new JsonArray();
        foreach (var item in message.Contents)
        {
            switch (item)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    content.Add(new JsonObject { ["text"] = text.Text });
                    break;
                case FunctionCallContent call:
                    content.Add(new JsonObject
                    {
                        ["toolUse"] = new JsonObject
                        {
                            ["toolUseId"] = call.CallId,
                            ["name"] = call.Name,
                            ["input"] = SerializeBedrockObject(call.Arguments, "value"),
                        },
                    });
                    break;
                case FunctionResultContent result:
                    content.Add(new JsonObject
                    {
                        ["toolResult"] = new JsonObject
                        {
                            ["toolUseId"] = result.CallId,
                            ["content"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    // Converse requires the json union member to contain an object,
                                    // while most built-in tools return a scalar string or number.
                                    ["json"] = SerializeBedrockObject(result.Result, "result"),
                                },
                            },
                        },
                    });
                    break;
            }
        }
        return content;
    }

    private static JsonObject SerializeBedrockObject(object? value, string wrapperProperty)
    {
        var node = value is JsonNode jsonNode
            ? jsonNode.DeepClone()
            : JsonSerializer.SerializeToNode(value);
        return node as JsonObject ?? new JsonObject { [wrapperProperty] = node };
    }

    private static JsonObject? BuildToolConfig(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
        {
            return null;
        }

        var definitions = new JsonArray();
        foreach (var function in tools.OfType<AIFunction>())
        {
            var schema = JsonNode.Parse(function.JsonSchema.GetRawText()) as JsonObject
                ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            definitions.Add(new JsonObject
            {
                ["toolSpec"] = new JsonObject
                {
                    ["name"] = function.Name,
                    ["description"] = function.Description,
                    ["inputSchema"] = new JsonObject { ["json"] = schema },
                },
            });
        }

        return definitions.Count == 0 ? null : new JsonObject { ["tools"] = definitions };
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
    /// Extract an assistant message from a non-streaming Converse response, preserving both text
    /// and tool-use blocks so the engine orchestration loop can invoke requested functions.
    /// </summary>
    public static ChatMessage ExtractResponseMessage(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("output", out var output)
            || !output.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return new ChatMessage(ChatRole.Assistant, string.Empty);
        }

        var contents = new List<AIContent>();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                contents.Add(new TextContent(text.GetString() ?? string.Empty));
                continue;
            }

            if (block.TryGetProperty("toolUse", out var toolUse)
                && toolUse.ValueKind == JsonValueKind.Object
                && toolUse.TryGetProperty("toolUseId", out var callId)
                && toolUse.TryGetProperty("name", out var name)
                && callId.ValueKind == JsonValueKind.String
                && name.ValueKind == JsonValueKind.String)
            {
                var arguments = toolUse.TryGetProperty("input", out var input)
                    && input.ValueKind == JsonValueKind.Object
                    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(input.GetRawText())
                    : null;
                contents.Add(new FunctionCallContent(
                    callId.GetString()!,
                    name.GetString()!,
                    arguments));
            }
        }

        return contents.Count == 0
            ? new ChatMessage(ChatRole.Assistant, string.Empty)
            : new ChatMessage(ChatRole.Assistant, contents);
    }

    /// <summary>Extract the text-only portion of a Converse response.</summary>
    public static string ExtractResponseText(string responseJson) => ExtractResponseMessage(responseJson).Text ?? string.Empty;

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
