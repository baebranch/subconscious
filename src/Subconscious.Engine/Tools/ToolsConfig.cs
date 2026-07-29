using System.Text.Json.Nodes;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Per-slug enablement state: a top-level toggle plus an optional per-tool override map.
/// A tool absent from <see cref="Tools"/> defaults to enabled.
/// </summary>
/// <param name="Enabled">False disables every tool in the slug regardless of <paramref name="Tools"/>.</param>
/// <param name="Tools">Per-tool-name enablement overrides.</param>
public sealed record SlugToolsConfig(
    bool Enabled = true,
    IReadOnlyDictionary<string, bool>? Tools = null)
{
    /// <summary>Whether <paramref name="toolName"/> is enabled, defaulting to true when unlisted.</summary>
    public bool IsToolEnabled(string toolName) =>
        Tools is null || !Tools.TryGetValue(toolName, out var enabled) || enabled;
}

/// <summary>
/// The persisted per-workspace / per-thread tool selection, mirroring the Python
/// <c>tools_config</c> JSON consumed by <c>BaseToolRegistry.get_tools_for_config</c>:
/// <code>{"builtin_enabled": bool, "builtin": {slug: {"enabled": bool, "tools": {name: bool}}}}</code>
///
/// <para>
/// Every level defaults to enabled when absent, preserving the legacy "all tools" behaviour for
/// unconfigured workspaces and threads. <see cref="BuiltinEnabled"/> is a master switch: when
/// false, no built-in tools are returned at all.
/// </para>
/// </summary>
public sealed record ToolsConfig(
    bool BuiltinEnabled = true,
    IReadOnlyDictionary<string, SlugToolsConfig>? Builtin = null)
{
    /// <summary>Default policy: every built-in tool enabled (an unconfigured workspace/thread).</summary>
    public static readonly ToolsConfig Default = new();

    /// <summary>Resolved config for <paramref name="slug"/>, defaulting to fully enabled.</summary>
    public SlugToolsConfig ForSlug(string slug) =>
        Builtin is not null && Builtin.TryGetValue(slug, out var cfg) ? cfg : new SlugToolsConfig();

    /// <summary>
    /// Parse the persisted JSON shape. A null or non-object node yields <see cref="Default"/>,
    /// matching the Python <c>config = config or {}</c> plus per-key <c>.get(..., True)</c>
    /// defaulting. Unrecognized keys are ignored rather than rejected, so a config written by a
    /// newer client version does not break an older engine.
    /// </summary>
    public static ToolsConfig FromJson(JsonNode? node)
    {
        if (node is not JsonObject root)
        {
            return Default;
        }

        var builtinEnabled = ReadBool(root, "builtin_enabled", defaultValue: true);
        Dictionary<string, SlugToolsConfig>? builtin = null;

        if (root["builtin"] is JsonObject builtinNode)
        {
            builtin = new Dictionary<string, SlugToolsConfig>(StringComparer.Ordinal);
            foreach (var (slug, slugNode) in builtinNode)
            {
                if (slugNode is not JsonObject slugObject)
                {
                    continue;
                }
                Dictionary<string, bool>? tools = null;
                if (slugObject["tools"] is JsonObject toolsNode)
                {
                    tools = new Dictionary<string, bool>(StringComparer.Ordinal);
                    foreach (var (toolName, stateNode) in toolsNode)
                    {
                        tools[toolName] = AsBool(stateNode) ?? true;
                    }
                }
                builtin[slug] = new SlugToolsConfig(ReadBool(slugObject, "enabled", true), tools);
            }
        }

        return new ToolsConfig(builtinEnabled, builtin);
    }

    private static bool ReadBool(JsonObject obj, string key, bool defaultValue) =>
        AsBool(obj[key]) ?? defaultValue;

    private static bool? AsBool(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }
        return value.TryGetValue<bool>(out var b) ? b : null;
    }
}
