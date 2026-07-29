using CommunityToolkit.Mvvm.ComponentModel;

namespace Subconscious.Desktop.ViewModels;

/// <summary>A single chat bubble: one persisted message, or the in-progress assistant reply being streamed.</summary>
public sealed partial class MessageViewModel : ViewModelBase
{
    public MessageViewModel(string role, string content)
    {
        Role = role;
        _content = content;
    }

    public string Role { get; }

    public bool IsUser => Role == "user";

    [ObservableProperty]
    private string _content;

    /// <summary>Append a streamed delta to this bubble's content (used for the live assistant reply).</summary>
    public void AppendDelta(string delta) => Content += delta;
}
