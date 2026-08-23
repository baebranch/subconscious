using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Chat;

namespace Subconscious.Mobile;

/// <summary>Phone-friendly adapter for the shared native chat transcript.</summary>
public sealed partial class ChatMessage : ObservableObject, IChatTranscriptMessage
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public ChatMessage(string role, string content, DateTime? createdAt = null)
    {
        Role = role;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        _content = content;
        UpdateToolSections(content);
    }

    public string Role { get; }
    public DateTime CreatedAt { get; }
    public string Timestamp => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsTool => string.Equals(Role, "tool", StringComparison.OrdinalIgnoreCase);
    public string ToolInput { get; private set; } = string.Empty;
    public string ToolOutput { get; private set; } = string.Empty;
    public string ToolTitle { get; private set; } = "Tool message";

    [ObservableProperty]
    private bool _isToolExpanded;

    public string ToolExpansionGlyph => IsToolExpanded ? "expanded" : "collapsed";

    partial void OnIsToolExpandedChanged(bool value) => OnPropertyChanged(nameof(ToolExpansionGlyph));

    [ObservableProperty]
    private string _content;

    partial void OnContentChanged(string value) => UpdateToolSections(value);

    [RelayCommand]
    private void ToggleToolExpanded() => IsToolExpanded = !IsToolExpanded;

    ICommand IChatTranscriptMessage.ToggleToolExpandedCommand => ToggleToolExpandedCommand;

    public void AppendDelta(string delta) => Content += delta;

    private void UpdateToolSections(string content)
    {
        if (!IsTool)
        {
            return;
        }

        var (input, output) = SplitToolPayload(content);
        ToolTitle = ResolveToolTitle(content);
        ToolInput = input;
        ToolOutput = output;
        OnPropertyChanged(nameof(ToolTitle));
        OnPropertyChanged(nameof(ToolInput));
        OnPropertyChanged(nameof(ToolOutput));
    }

    private static (string Input, string Output) SplitToolPayload(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (string.Empty, string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return (FormatJson(document.RootElement), string.Empty);
            }

            var input = FindSection(document.RootElement, "input", "arguments", "parameters", "request");
            var output = FindSection(document.RootElement, "output", "result", "response", "return");
            return input.HasValue || output.HasValue
                ? (input.HasValue ? FormatJson(input.Value) : "(no input)",
                   output.HasValue ? FormatJson(output.Value) : "(no output)")
                : (FormatJson(document.RootElement), string.Empty);
        }
        catch (JsonException)
        {
            return (content, string.Empty);
        }
    }

    private static string ResolveToolTitle(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "Tool message";
            }

            var candidate = FindSection(document.RootElement,
                "toolName", "tool_name", "functionName", "function_name", "name", "tool", "function");
            if (candidate?.ValueKind == JsonValueKind.Object)
            {
                candidate = FindSection(candidate.Value,
                    "toolName", "tool_name", "functionName", "function_name", "name");
            }

            return candidate?.ValueKind == JsonValueKind.String
                ? HumanizeToolName(candidate.Value.GetString())
                : "Tool message";
        }
        catch (JsonException)
        {
            return "Tool message";
        }
    }

    private static string HumanizeToolName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Tool message";
        }

        var spaced = string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character) && char.IsLower(name[index - 1])
                ? $" {character}"
                : character.ToString()))
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ');
        var words = string.Join(" ", spaced.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words.ToLower(CultureInfo.CurrentCulture));
    }

    private static JsonElement? FindSection(JsonElement payload, params string[] names)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string FormatJson(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : JsonSerializer.Serialize(value, PrettyJson);
}
