using CommunityToolkit.Mvvm.ComponentModel;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Independent in-memory state for one Engine-backed workspace file tab.</summary>
public sealed partial class FileEditorTab : ObservableObject
{
    public FileEditorTab(string workspaceUuid, int rootIndex, string relativePath, string displayName, string content)
    {
        WorkspaceUuid = workspaceUuid;
        RootIndex = rootIndex;
        RelativePath = relativePath;
        DisplayName = displayName;
        Content = content;
    }

    public string WorkspaceUuid { get; }
    public int RootIndex { get; }
    public string RelativePath { get; }
    public string DisplayName { get; }
    public bool IsMarkdown => Path.GetExtension(RelativePath) is ".md" or ".markdown";

    [ObservableProperty] private string _content;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>A selectable configured workspace root for the new-file form.</summary>
public sealed record WorkspaceFileRootOption(int RootIndex, string DisplayName);
