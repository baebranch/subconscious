using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Subconscious.Chat;

namespace Subconscious.Mobile;

/// <summary>Phone-friendly adapter for the shared native chat transcript.</summary>
public sealed partial class ChatMessage : ObservableObject, IChatTranscriptMessage
{
    public ChatMessage(string role, string content, DateTime? createdAt = null)
    {
        Role = role;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        _content = content;
    }

    public string Role { get; }
    public DateTime CreatedAt { get; }
    public string Timestamp => CreatedAt.ToLocalTime().ToString("g");
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsTool => string.Equals(Role, "tool", StringComparison.OrdinalIgnoreCase);
    public string ToolInput => Content;
    public string ToolOutput => string.Empty;
    public string ToolTitle => "Tool message";
    public bool IsToolExpanded => false;
    public string ToolExpansionGlyph => string.Empty;
    public ICommand ToggleToolExpandedCommand { get; } = new Command(() => { });

    [ObservableProperty]
    private string _content;

    public void AppendDelta(string delta) => Content += delta;
}
