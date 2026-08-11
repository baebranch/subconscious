using System.ComponentModel;

namespace Subconscious.WYSIWYG;

public enum EditorDocumentKind
{
    Text,
    Markdown,
    Code,
    Document,
}

/// <summary>The UI-neutral document contract consumed by the reusable editor workspace.</summary>
public interface IEditorDocument : INotifyPropertyChanged
{
    string DocumentId { get; }
    string DisplayName { get; }
    string Content { get; set; }
    string Language { get; }
    EditorDocumentKind Kind { get; }
    bool IsDirty { get; }
    bool IsReadOnly { get; }
}

/// <summary>Raised only for the document identity that produced a native editor change.</summary>
public sealed class EditorTextChangedEventArgs(string documentId, string text) : EventArgs
{
    public string DocumentId { get; } = documentId;
    public string Text { get; } = text;
}
