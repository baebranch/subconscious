using System.Text.Json;
using System.Text.Json.Nodes;

namespace Subconscious.Engine.Api.Services;

/// <summary>Lossless JSON-object merge and delta helpers for persisted tool configuration.</summary>
internal static class ToolConfigJson
{
    public static JsonObject RequireObject(JsonNode? config)
    {
        if (config is not JsonObject obj)
        {
            throw new ArgumentException("Tool configuration must be a JSON object.");
        }
        return (JsonObject)obj.DeepClone();
    }

    public static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Stored tool configuration is not valid JSON.", exception);
        }
    }

    public static string Serialize(JsonNode config) => config.ToJsonString();

    public static string? Resolve(string? workspaceJson, string? threadOverrideJson) =>
        string.IsNullOrWhiteSpace(threadOverrideJson)
            ? workspaceJson
            : Serialize(ResolveNode(workspaceJson, threadOverrideJson)!);

    public static JsonNode? ResolveNode(string? workspaceJson, string? threadOverrideJson)
    {
        var workspace = ParseObject(workspaceJson);
        if (string.IsNullOrWhiteSpace(threadOverrideJson))
        {
            return workspace;
        }
        return Merge(workspace, ParseObject(threadOverrideJson)!);
    }

    public static JsonObject? Delta(JsonObject? baseline, JsonObject desired)
    {
        var delta = new JsonObject();
        foreach (var (key, desiredValue) in desired)
        {
            JsonNode? baselineValue = null;
            baseline?.TryGetPropertyValue(key, out baselineValue);
            if (desiredValue is JsonObject desiredObject && baselineValue is JsonObject baselineObject)
            {
                var nested = Delta(baselineObject, desiredObject);
                if (nested is not null)
                {
                    delta[key] = nested;
                }
            }
            else if (!JsonNode.DeepEquals(baselineValue, desiredValue))
            {
                delta[key] = desiredValue?.DeepClone();
            }
        }
        return delta.Count == 0 ? null : delta;
    }

    private static JsonObject? ParseObject(string? json)
    {
        var node = Parse(json);
        return node switch
        {
            null => null,
            JsonObject obj => obj,
            _ => throw new InvalidOperationException("Stored tool configuration must be a JSON object."),
        };
    }

    private static JsonObject Merge(JsonObject? baseline, JsonObject overrideConfig)
    {
        var result = baseline is null ? new JsonObject() : (JsonObject)baseline.DeepClone();
        foreach (var (key, overrideValue) in overrideConfig)
        {
            if (overrideValue is JsonObject overrideObject && result[key] is JsonObject baselineObject)
            {
                result[key] = Merge(baselineObject, overrideObject);
            }
            else
            {
                result[key] = overrideValue?.DeepClone();
            }
        }
        return result;
    }
}
