using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Subconscious.Desktop.ViewModels;

/// <summary>A persisted chat message, or the in-progress assistant reply being streamed.</summary>
public sealed partial class MessageViewModel : ViewModelBase
{
    public MessageViewModel(string role, string content, DateTime? createdAt = null)
    {
        Role = role;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        _content = content;
    }

    /// <summary>The Engine's message role: user, assistant, system, or tool.</summary>
    public string Role { get; }

    /// <summary>The timestamp supplied by the Engine for history messages, or the local creation
    /// time for the optimistic user/streaming-assistant messages.</summary>
    public DateTime CreatedAt { get; }

    public string Timestamp => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);

    public bool IsTool => string.Equals(Role, "tool", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _content;

    /// <summary>Append a streamed delta to this bubble's content.</summary>
    public void AppendDelta(string delta) => Content += delta;
}
