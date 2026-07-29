using Microsoft.Extensions.AI;
using Subconscious.Engine.Approval;
using Subconscious.Engine.Tools.Builtin;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Maps tool slugs to <see cref="IToolModule"/>s and resolves the enabled tool set for a turn.
/// Port of <c>tools/__init__.py</c>'s <c>BaseToolRegistry</c>: only cross-platform tools live
/// here; platform registries (desktop/mobile/server) subclass and add their own.
///
/// <para>
/// Slug ordering is insertion-ordered (Python dicts are too), so the tool list handed to a model
/// is deterministic — worth preserving because it affects prompt caching.
/// </para>
///
/// <para>
/// <b>Currently registered:</b> "time", "calculator", and "weather". The DB-backed modules ("todo",
/// "memory", "notes", "contacts", "knowledge") are not registered yet — they are blocked on the
/// Phase 1 EF Core data layer, which has not landed (see translation.md). <see cref="Register"/>
/// is how they will be added; nothing else about this class needs to change.
/// </para>
/// </summary>
public class BaseToolRegistry
{
    private readonly Dictionary<string, IToolModule> _modules = new(StringComparer.Ordinal);
    private readonly List<string> _slugOrder = [];

    public BaseToolRegistry()
    {
        LoadBaseTools();
    }

    /// <summary>
    /// Register the tool modules that are safe on every platform (no OS/desktop APIs).
    /// Equivalent to <c>_load_base_tools</c>.
    /// </summary>
    private void LoadBaseTools()
    {
        Register(new TimeToolModule());
        Register(new CalculatorToolModule());
        Register(new WeatherToolModule());
    }

    /// <summary>
    /// Register (or replace) the module under <paramref name="module"/>'s slug. Equivalent to
    /// Python's <c>register(slug, tools)</c>, used by platform registries and user-defined
    /// tool sets.
    /// </summary>
    public void Register(IToolModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!_modules.ContainsKey(module.Slug))
        {
            _slugOrder.Add(module.Slug);
        }
        _modules[module.Slug] = module;
    }

    /// <summary>All registered slugs, in registration order. Equivalent to <c>all_slugs()</c>.</summary>
    public IReadOnlyList<string> AllSlugs() => _slugOrder;

    /// <summary>
    /// Flat tool list for the requested slugs, in the order the slugs were requested.
    /// Unknown slugs are skipped, matching Python's <c>self._registry.get(slug, [])</c>.
    /// </summary>
    public IReadOnlyList<AIFunction> GetTools(IEnumerable<string> slugs, EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(slugs);
        ArgumentNullException.ThrowIfNull(context);

        var result = new List<AIFunction>();
        foreach (var slug in slugs)
        {
            if (_modules.TryGetValue(slug, out var module))
            {
                result.AddRange(module.CreateTools(context));
            }
        }
        return result;
    }

    /// <summary>
    /// Resolve the enabled tools for <paramref name="config"/>, in registration order.
    /// Port of <c>get_tools_for_config</c>: the <see cref="ToolsConfig.BuiltinEnabled"/> master
    /// flag short-circuits to an empty list, a disabled slug skips all of its tools, and
    /// anything unlisted defaults to enabled.
    /// </summary>
    public IReadOnlyList<AIFunction> GetToolsForConfig(ToolsConfig config, EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        config ??= ToolsConfig.Default;

        if (!config.BuiltinEnabled)
        {
            return [];
        }

        var result = new List<AIFunction>();
        foreach (var slug in _slugOrder)
        {
            var slugConfig = config.ForSlug(slug);
            if (!slugConfig.Enabled)
            {
                continue;
            }
            foreach (var tool in _modules[slug].CreateTools(context))
            {
                if (slugConfig.IsToolEnabled(tool.Name))
                {
                    result.Add(tool);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Hierarchy of built-in tools for the UI's toggle tree, keyed by slug. Port of
    /// <c>catalog()</c>. Built from <see cref="EngineContext.ForCatalog"/> so the catalog is
    /// always derived from the real tool definitions instead of a parallel hand-maintained list.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ToolCatalogEntry>> Catalog()
    {
        var result = new Dictionary<string, IReadOnlyList<ToolCatalogEntry>>(StringComparer.Ordinal);
        foreach (var slug in _slugOrder)
        {
            var entries = new List<ToolCatalogEntry>();
            foreach (var tool in _modules[slug].CreateTools(EngineContext.ForCatalog))
            {
                entries.Add(new ToolCatalogEntry(
                    tool.Name,
                    FirstLine(tool.Description),
                    OperationClassifier.Classify(tool.Name)));
            }
            result[slug] = entries;
        }
        return result;
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        return (newline < 0 ? trimmed : trimmed[..newline]).Trim();
    }
}
