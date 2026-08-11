using CommunityToolkit.Mvvm.ComponentModel;
using Subconscious.WYSIWYG;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Independent Engine metadata and editable state for one reusable editor document tab.</summary>
public sealed partial class FileEditorTab : ObservableObject, IEditorDocument
{
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csx", ".css", ".fs", ".fsx", ".html", ".htm", ".java", ".js", ".jsx", ".json",
        ".py", ".rb", ".rs", ".sh", ".sql", ".ts", ".tsx", ".xml", ".xaml", ".yaml", ".yml",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".odp", ".ods", ".odt", ".pdf", ".potx", ".ppsx", ".ppt", ".pptx", ".rtf", ".xls", ".xlsx",
    };

    public FileEditorTab(string workspaceUuid, int rootIndex, string relativePath, string displayName, string content, bool isReadOnly = false)
    {
        WorkspaceUuid = workspaceUuid;
        RootIndex = rootIndex;
        RelativePath = relativePath;
        DisplayName = displayName;
        _content = content;
        IsReadOnly = isReadOnly;
        Kind = Classify(relativePath);
    }

    public string WorkspaceUuid { get; }
    public int RootIndex { get; }
    public string RelativePath { get; }
    public string DisplayName { get; }
    public string DocumentId => $"{WorkspaceUuid}:{RootIndex}:{RelativePath}";
    public bool IsReadOnly { get; }
    public EditorDocumentKind Kind { get; }
    public bool IsMarkdown => Kind == EditorDocumentKind.Markdown;
    public bool IsCode => Kind == EditorDocumentKind.Code;
    public bool IsText => Kind == EditorDocumentKind.Text;
    public bool IsDocument => Kind == EditorDocumentKind.Document;
    public string Language => Path.GetExtension(RelativePath).TrimStart('.').ToLowerInvariant();
    public string CodeLanguage => Language;

    public static EditorDocumentKind Classify(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return EditorDocumentKind.Markdown;
        }

        return DocumentExtensions.Contains(extension) ? EditorDocumentKind.Document
            : CodeExtensions.Contains(extension) ? EditorDocumentKind.Code
            : EditorDocumentKind.Text;
    }

    [ObservableProperty] private string _content;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSelected;

    partial void OnContentChanged(string value) => IsDirty = true;
}
