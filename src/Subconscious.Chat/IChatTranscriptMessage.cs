using System.ComponentModel;
using System.Windows.Input;

namespace Subconscious.Chat;

/// <summary>UI-neutral contract for a message displayed in a chat transcript.</summary>
public interface IChatTranscriptMessage : INotifyPropertyChanged
{
    string Role { get; }
    DateTime CreatedAt { get; }
    string Timestamp { get; }
    string Content { get; }
    bool IsUser { get; }
    bool IsTool { get; }
    string ToolInput { get; }
    string ToolOutput { get; }
    string ToolTitle { get; }
    bool IsToolExpanded { get; }
    string ToolExpansionGlyph { get; }
    ICommand ToggleToolExpandedCommand { get; }
}
