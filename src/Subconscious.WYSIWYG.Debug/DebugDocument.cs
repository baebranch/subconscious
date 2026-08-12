using System.ComponentModel;
using System.Runtime.CompilerServices;
using Subconscious.WYSIWYG;

namespace Subconscious.WYSIWYG.Debug;

public sealed class DebugDocument : IEditorDocument
{
    private string _content;
    private bool _isDirty;
    private bool _resetting;

    public DebugDocument(string id, string displayName, EditorDocumentKind kind, string language, string content)
    {
        DocumentId = id;
        DisplayName = displayName;
        Kind = kind;
        Language = language;
        InitialContent = content;
        _content = content;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DocumentId { get; }
    public string DisplayName { get; }
    public string InitialContent { get; }
    public EditorDocumentKind Kind { get; }
    public string Language { get; }
    public bool IsReadOnly => false;
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            OnPropertyChanged();
            if (!_resetting) IsDirty = true;
        }
    }

    public void Reset()
    {
        _resetting = true;
        try { Content = InitialContent; }
        finally { _resetting = false; }
        IsDirty = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
