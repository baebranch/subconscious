using CommunityToolkit.Mvvm.ComponentModel;

namespace Subconscious.Desktop.ViewModels;

/// <summary>A visible row in the engine-backed, workspace-scoped file tree.</summary>
public sealed partial class FileTreeNode : ObservableObject
{
    public FileTreeNode(int rootIndex, string relativePath, string displayName, bool isDirectory, int depth)
    {
        RootIndex = rootIndex;
        RelativePath = relativePath;
        DisplayName = displayName;
        IsDirectory = isDirectory;
        Depth = depth;
        Indentation = new Thickness(12 + depth * 16, 0, 8, 0);
    }

    /// <summary>Index into the selected workspace's persisted directory list.</summary>
    public int RootIndex { get; }
    /// <summary>Engine-validated path relative to <see cref="RootIndex"/>.</summary>
    public string RelativePath { get; }
    public bool IsDirectory { get; }
    public int Depth { get; }
    public string DisplayName { get; }
    public Thickness Indentation { get; }
    public List<FileTreeNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    public bool IsCollapsedDirectory => IsDirectory && !IsExpanded && !IsLoading;
    public bool ChildrenLoaded { get; set; }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(IsCollapsedDirectory));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsCollapsedDirectory));
}
