using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Ephemeral expansion state for the policy editor; it is deliberately excluded from persisted policy JSON.</summary>
public sealed record ToolPolicyEditorExpansionState(
    bool BuiltinToolsExpanded,
    bool CustomToolsExpanded,
    IReadOnlyDictionary<string, bool> BuiltinGroupExpansions);

/// <summary>Editable, lossless projection of the engine tool-policy JSON and live tool catalog.</summary>
public sealed partial class ToolPolicyEditorViewModel : ViewModelBase
{
    private JsonObject _rawConfig = new();
    private bool _isPopulating;

    public ObservableCollection<ToolPolicyGroupViewModel> BuiltinGroups { get; } = [];
    public ObservableCollection<ConfiguredToolPolicyViewModel> ConfiguredTools { get; } = [];

    public event EventHandler? Changed;

    [ObservableProperty] private bool _builtinToolsEnabled = true;
    [ObservableProperty] private bool _customToolsEnabled = true;
    [ObservableProperty] private bool _isBuiltinToolsExpanded;
    [ObservableProperty] private bool _isCustomToolsExpanded;

    public string BuiltinToolsExpansionGlyph => IsBuiltinToolsExpanded ? "⌃" : "⌄";
    public string CustomToolsExpansionGlyph => IsCustomToolsExpanded ? "⌃" : "⌄";

    [RelayCommand] private void ToggleBuiltinToolsExpanded() => IsBuiltinToolsExpanded = !IsBuiltinToolsExpanded;
    [RelayCommand] private void ToggleCustomToolsExpanded() => IsCustomToolsExpanded = !IsCustomToolsExpanded;

    /// <summary>Captures UI-only card expansion without adding it to the engine policy.</summary>
    public ToolPolicyEditorExpansionState CaptureExpansionState() => new(
        IsBuiltinToolsExpanded,
        IsCustomToolsExpanded,
        BuiltinGroups.ToDictionary(group => group.Slug, group => group.IsExpanded, StringComparer.Ordinal));

    /// <summary>Reapplies UI-only card expansion after <see cref="Populate"/> creates fresh group view models.</summary>
    public void RestoreExpansionState(ToolPolicyEditorExpansionState? state)
    {
        if (state is null)
        {
            return;
        }

        IsBuiltinToolsExpanded = state.BuiltinToolsExpanded;
        IsCustomToolsExpanded = state.CustomToolsExpanded;
        foreach (var group in BuiltinGroups)
        {
            group.IsExpanded = state.BuiltinGroupExpansions.TryGetValue(group.Slug, out var isExpanded) && isExpanded;
        }
    }

    public void Populate(ToolCatalog catalog, JsonNode? rawConfig)
    {
        _isPopulating = true;
        try
        {
            _rawConfig = rawConfig is JsonObject root ? (JsonObject)root.DeepClone() : new JsonObject();
            BuiltinToolsEnabled = ReadBool(_rawConfig["builtin_enabled"], true);
            CustomToolsEnabled = ReadBool(_rawConfig["configured_enabled"], true);
            var builtin = _rawConfig["builtin"] as JsonObject;
            var custom = _rawConfig["configured"] as JsonObject;

            BuiltinGroups.Clear();
            foreach (var (slug, entries) in catalog.Builtin.OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var savedGroup = builtin?[slug] as JsonObject;
                var group = new ToolPolicyGroupViewModel(slug, entries, savedGroup, NotifyChanged);
                group.SetRootEnabled(BuiltinToolsEnabled);
                BuiltinGroups.Add(group);
            }

            ConfiguredTools.Clear();
            foreach (var tool in catalog.Configured)
            {
                var configuredTool = new ConfiguredToolPolicyViewModel(tool, ReadBool(custom?[tool.Uuid], true), NotifyChanged);
                configuredTool.SetParentEnabled(CustomToolsEnabled);
                ConfiguredTools.Add(configuredTool);
            }
        }
        finally
        {
            _isPopulating = false;
        }
    }

    /// <summary>Produces a complete desired policy while leaving unfamiliar root/group/tool keys untouched.</summary>
    public JsonObject SerializeDesiredConfig()
    {
        var result = (JsonObject)_rawConfig.DeepClone();
        result["builtin_enabled"] = BuiltinToolsEnabled;
        result["configured_enabled"] = CustomToolsEnabled;

        var builtin = result["builtin"] is JsonObject existingBuiltin
            ? (JsonObject)existingBuiltin.DeepClone() : new JsonObject();
        foreach (var group in BuiltinGroups)
        {
            var groupConfig = builtin[group.Slug] is JsonObject existingGroup
                ? (JsonObject)existingGroup.DeepClone() : new JsonObject();
            groupConfig["enabled"] = group.IsEnabled;
            var tools = groupConfig["tools"] is JsonObject existingTools
                ? (JsonObject)existingTools.DeepClone() : new JsonObject();
            foreach (var tool in group.Tools)
            {
                tools[tool.Name] = tool.IsEnabled;
            }
            groupConfig["tools"] = tools;
            builtin[group.Slug] = groupConfig;
        }
        result["builtin"] = builtin;

        var custom = result["configured"] is JsonObject existingCustom
            ? (JsonObject)existingCustom.DeepClone() : new JsonObject();
        foreach (var tool in ConfiguredTools)
        {
            custom[tool.Uuid] = tool.IsEnabled;
        }
        result["configured"] = custom;
        return result;
    }

    partial void OnBuiltinToolsEnabledChanged(bool value)
    {
        foreach (var group in BuiltinGroups)
        {
            group.SetRootEnabled(value);
        }
        NotifyChanged();
    }

    partial void OnCustomToolsEnabledChanged(bool value)
    {
        foreach (var tool in ConfiguredTools)
        {
            tool.SetParentEnabled(value);
        }
        NotifyChanged();
    }

    partial void OnIsBuiltinToolsExpandedChanged(bool value) => OnPropertyChanged(nameof(BuiltinToolsExpansionGlyph));
    partial void OnIsCustomToolsExpandedChanged(bool value) => OnPropertyChanged(nameof(CustomToolsExpansionGlyph));

    private void NotifyChanged()
    {
        if (!_isPopulating)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static bool ReadBool(JsonNode? node, bool fallback) =>
        node is JsonValue value && value.TryGetValue<bool>(out var enabled) ? enabled : fallback;
}

/// <summary>One real built-in catalog slug and its slug/name-keyed switches.</summary>
public sealed partial class ToolPolicyGroupViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _rootEnabled = true;

    public ToolPolicyGroupViewModel(
        string slug,
        IEnumerable<BuiltinToolCatalogEntry> tools,
        JsonObject? savedConfig,
        Action changed)
    {
        Slug = slug;
        _changed = changed;
        IsEnabled = ToolPolicyEditorViewModel.ReadBool(savedConfig?["enabled"], true);
        var savedTools = savedConfig?["tools"] as JsonObject;
        foreach (var tool in tools.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            Tools.Add(new BuiltinToolPolicyViewModel(tool, ToolPolicyEditorViewModel.ReadBool(savedTools?[tool.Name], true), changed));
        }
    }

    public string Slug { get; }
    /// <summary>Friendly group text while tool keys remain their stable lower-case slugs.</summary>
    public string DisplayName => Slug switch
    {
        "todo" => "To-Do Tools",
        _ => $"{char.ToUpperInvariant(Slug[0])}{Slug[1..]} Tools",
    };
    public string ExpansionGlyph => IsExpanded ? "⌃" : "⌄";
    /// <summary>Whether this group switch is available under the Builtin Tools root switch.</summary>
    public bool IsParentEnabled => _rootEnabled;
    public ObservableCollection<BuiltinToolPolicyViewModel> Tools { get; } = [];
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isExpanded;

    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;

    internal void SetRootEnabled(bool value)
    {
        if (_rootEnabled == value)
        {
            return;
        }

        _rootEnabled = value;
        OnPropertyChanged(nameof(IsParentEnabled));
        UpdateToolAvailability();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        UpdateToolAvailability();
        _changed();
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpansionGlyph));

    private void UpdateToolAvailability()
    {
        var enabled = _rootEnabled && IsEnabled;
        foreach (var tool in Tools)
        {
            tool.SetParentEnabled(enabled);
        }
    }
}

/// <summary>One catalog tool addressed by its stable built-in name.</summary>
public sealed partial class BuiltinToolPolicyViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _parentEnabled = true;

    public BuiltinToolPolicyViewModel(BuiltinToolCatalogEntry entry, bool isEnabled, Action changed)
    {
        Name = entry.Name;
        Description = entry.Doc;
        _changed = changed;
        IsEnabled = isEnabled;
    }

    public string Name { get; }
    public string Description { get; }
    /// <summary>Derived UI state; the stored switch value remains intact while a parent is off.</summary>
    public bool IsParentEnabled => _parentEnabled;
    [ObservableProperty] private bool _isEnabled;

    internal void SetParentEnabled(bool value)
    {
        if (_parentEnabled == value)
        {
            return;
        }

        _parentEnabled = value;
        OnPropertyChanged(nameof(IsParentEnabled));
    }

    partial void OnIsEnabledChanged(bool value) => _changed();
}

/// <summary>One configured tool addressed by its registry UUID, never by its mutable name or alias.</summary>
public sealed partial class ConfiguredToolPolicyViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _parentEnabled = true;

    public ConfiguredToolPolicyViewModel(ToolRegistry tool, bool isEnabled, Action changed)
    {
        Uuid = tool.Uuid;
        DisplayName = string.IsNullOrWhiteSpace(tool.Alias) ? tool.Name : tool.Alias;
        Detail = tool.ToolType;
        _changed = changed;
        IsEnabled = isEnabled;
    }

    public string Uuid { get; }
    public string DisplayName { get; }
    public string Detail { get; }
    /// <summary>Derived UI state; the stored switch value remains intact while the root is off.</summary>
    public bool IsParentEnabled => _parentEnabled;
    [ObservableProperty] private bool _isEnabled;

    internal void SetParentEnabled(bool value)
    {
        if (_parentEnabled == value)
        {
            return;
        }

        _parentEnabled = value;
        OnPropertyChanged(nameof(IsParentEnabled));
    }

    partial void OnIsEnabledChanged(bool value) => _changed();
}
