using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Subconscious.Chat;

namespace Subconscious.Chat.Debug;

public class SampleMessage : IChatTranscriptMessage
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };
    private string _content;
    private bool _isToolExpanded;

    public SampleMessage(string role, string content, DateTime createdAt)
    {
        Role = role;
        CreatedAt = createdAt;
        _content = content;
        ToggleToolExpandedCommand = new DelegateCommand(() => IsToolExpanded = !IsToolExpanded);
        UpdateToolSections();
    }

    public string Role { get; }
    public DateTime CreatedAt { get; }
    public string Timestamp => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public bool IsUser => Role.Equals("user", StringComparison.OrdinalIgnoreCase);
    public bool IsTool => Role.Equals("tool", StringComparison.OrdinalIgnoreCase);
    public string ToolInput { get; private set; } = string.Empty;
    public string ToolOutput { get; private set; } = string.Empty;
    public string ToolTitle { get; private set; } = "Tool message";
    public string ToolExpansionGlyph => IsToolExpanded ? "▴" : "▾";
    public ICommand ToggleToolExpandedCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            OnPropertyChanged();
            UpdateToolSections();
        }
    }

    public bool IsToolExpanded
    {
        get => _isToolExpanded;
        set
        {
            if (_isToolExpanded == value) return;
            _isToolExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToolExpansionGlyph));
        }
    }

    private void UpdateToolSections()
    {
        if (!IsTool) return;

        try
        {
            using var document = JsonDocument.Parse(Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                ToolInput = Format(document.RootElement);
                ToolOutput = string.Empty;
                ToolTitle = "Tool message";
            }
            else
            {
                var root = document.RootElement;
                ToolTitle = Humanize(Find(root, "toolName", "tool_name", "name")?.GetString());
                ToolInput = Format(Find(root, "input", "arguments", "parameters", "request"));
                ToolOutput = Format(Find(root, "output", "result", "response", "return"));
                if (string.IsNullOrEmpty(ToolInput) && string.IsNullOrEmpty(ToolOutput))
                {
                    ToolInput = Format(root);
                }
            }
        }
        catch (JsonException)
        {
            ToolTitle = "Tool message";
            ToolInput = Content;
            ToolOutput = string.Empty;
        }

        OnPropertyChanged(nameof(ToolTitle));
        OnPropertyChanged(nameof(ToolInput));
        OnPropertyChanged(nameof(ToolOutput));
    }

    private static JsonElement? Find(JsonElement value, params string[] names)
    {
        foreach (var property in value.EnumerateObject())
        {
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value;
            }
        }
        return null;
    }

    private static string Format(JsonElement? value) => value switch
    {
        null => string.Empty,
        { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        { } element => JsonSerializer.Serialize(element, PrettyJson),
    };

    private static string Humanize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Tool message";
        var spaced = string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character) && char.IsLower(name[index - 1])
                ? $" {character}" : character.ToString())).Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(spaced.ToLower(CultureInfo.CurrentCulture));
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
