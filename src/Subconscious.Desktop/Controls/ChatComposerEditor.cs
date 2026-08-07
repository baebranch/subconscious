using System.Windows.Input;

namespace Subconscious.Desktop.Controls;

/// <summary>A native multiline editor used only inside the chat omnibox.</summary>
public sealed class ChatComposerEditor : Editor
{
    public static readonly BindableProperty SubmitCommandProperty = BindableProperty.Create(
        nameof(SubmitCommand),
        typeof(ICommand),
        typeof(ChatComposerEditor));

    /// <summary>Executed by the Windows handler for an unmodified Enter key. Shift+Enter remains
    /// available for inserting a newline into the multiline prompt.</summary>
    public ICommand? SubmitCommand
    {
        get => (ICommand?)GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }
}
