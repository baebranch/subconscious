using System.Collections.Specialized;
using Subconscious.Desktop.Engine;
using Subconscious.Desktop.Services;

namespace Subconscious.Desktop.ViewModels;

public sealed partial class FileWorkspaceViewModel
{
    private bool _isRestoringNavigation;

    /// <summary>Raised after a restorable file-tree or editor-tab navigation change.</summary>
    public event EventHandler? NavigationStateChanged;

    public FileWorkspaceViewModel()
    {
        OpenFiles.CollectionChanged += OnOpenFilesChanged;
        VisibleNodes.CollectionChanged += OnVisibleNodesChanged;
        PropertyChanged += OnFileWorkspacePropertyChanged;
    }

    /// <summary>Captures only engine-backed, clean document identities; file contents remain engine-owned.</summary>
    public FileWorkspaceNavigationState CaptureNavigationState()
    {
        var tabs = OpenFiles
            .Where(IsPersistable)
            .Select(tab => new FileEditorDocumentReference(tab.WorkspaceUuid, tab.RootIndex, tab.RelativePath))
            .Distinct()
            .ToArray();
        var selected = SelectedTab is { } tab && IsPersistable(tab)
            ? new FileEditorDocumentReference(tab.WorkspaceUuid, tab.RootIndex, tab.RelativePath)
            : null;

        return new FileWorkspaceNavigationState
        {
            TreeWorkspaceUuid = _treeWorkspaceUuid,
            TreeDirectories = _treeDirectories,
            ExpandedDirectories = VisibleNodes
                .Where(node => node.IsDirectory && node.IsExpanded)
                .Select(node => new FileTreeDirectoryReference(node.RootIndex, node.RelativePath))
                .Distinct()
                .ToArray(),
            OpenTabs = tabs,
            SelectedTab = selected is not null && tabs.Contains(selected) ? selected : null,
        };
    }

    /// <summary>Restores tabs from the engine and expands only matching directories in the active workspace.</summary>
    public async Task RestoreNavigationStateAsync(
        FileWorkspaceNavigationState state,
        IEnumerable<Workspace> workspaces,
        Workspace? activeWorkspace)
    {
        if ((state.OpenTabs?.Count ?? 0) == 0 && string.IsNullOrWhiteSpace(state.TreeWorkspaceUuid))
        {
            return;
        }

        _isRestoringNavigation = true;
        try
        {
            var workspaceByUuid = workspaces
                .Where(workspace => !string.IsNullOrWhiteSpace(workspace.Uuid))
                .GroupBy(workspace => workspace.Uuid, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var document in (state.OpenTabs ?? []).Distinct())
            {
                if (!workspaceByUuid.TryGetValue(document.WorkspaceUuid, out var workspace)
                    || !IsConfiguredRoot(workspace, document.RootIndex)
                    || OpenFiles.Any(tab => SameDocument(tab, document)))
                {
                    continue;
                }

                try
                {
                    await EnsureRestClientAsync();
                    var file = await _client.ReadWorkspaceFileAsync(
                        workspace.Uuid, document.RootIndex, document.RelativePath);
                    OpenFiles.Add(new FileEditorTab(
                        workspace.Uuid,
                        document.RootIndex,
                        document.RelativePath,
                        Path.GetFileName(document.RelativePath),
                        file.Content));
                }
                catch (Exception)
                {
                    // A deleted, inaccessible, or newly-disallowed document is simply omitted.
                }
            }

            var selected = state.SelectedTab is { } selectedReference
                ? OpenFiles.FirstOrDefault(tab => SameDocument(tab, selectedReference))
                : null;
            SelectedTab = selected ?? OpenFiles.FirstOrDefault(IsPersistable);

            if (activeWorkspace is not null
                && string.Equals(activeWorkspace.Uuid, state.TreeWorkspaceUuid, StringComparison.Ordinal)
                && string.Equals(activeWorkspace.Directories, state.TreeDirectories, StringComparison.Ordinal))
            {
                await LoadWorkspaceAsync(activeWorkspace);
                foreach (var directory in (state.ExpandedDirectories ?? [])
                    .Distinct()
                    .OrderBy(entry => entry.RootIndex)
                    .ThenBy(entry => PathDepth(entry.RelativePath)))
                {
                    var node = VisibleNodes.FirstOrDefault(candidate => candidate.IsDirectory
                        && candidate.RootIndex == directory.RootIndex
                        && string.Equals(candidate.RelativePath, directory.RelativePath, StringComparison.Ordinal));
                    if (node is not null && !node.IsExpanded)
                    {
                        await ToggleDirectoryAsync(node);
                    }
                }
            }
        }
        finally
        {
            _isRestoringNavigation = false;
            RaiseNavigationStateChanged();
        }
    }

    private void OnOpenFilesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null)
        {
            foreach (var tab in args.OldItems.OfType<FileEditorTab>())
            {
                tab.PropertyChanged -= OnTabPropertyChanged;
            }
        }
        if (args.NewItems is not null)
        {
            foreach (var tab in args.NewItems.OfType<FileEditorTab>())
            {
                tab.PropertyChanged += OnTabPropertyChanged;
            }
        }
        RaiseNavigationStateChanged();
    }

    private void OnVisibleNodesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.OldItems is not null)
        {
            foreach (var node in args.OldItems.OfType<FileTreeNode>())
            {
                node.PropertyChanged -= OnTreeNodePropertyChanged;
            }
        }
        if (args.NewItems is not null)
        {
            foreach (var node in args.NewItems.OfType<FileTreeNode>())
            {
                node.PropertyChanged += OnTreeNodePropertyChanged;
            }
        }
        RaiseNavigationStateChanged();
    }

    private void OnTreeNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FileTreeNode.IsExpanded))
        {
            RaiseNavigationStateChanged();
        }
    }

    private void OnFileWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SelectedTab))
        {
            RaiseNavigationStateChanged();
        }
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FileEditorTab.IsDirty))
        {
            RaiseNavigationStateChanged();
        }
    }

    private static bool IsPersistable(FileEditorTab tab) =>
        !tab.IsDirty
        && tab.RootIndex >= 0
        && !string.IsNullOrWhiteSpace(tab.WorkspaceUuid)
        && !string.IsNullOrWhiteSpace(tab.RelativePath);

    private static bool SameDocument(FileEditorTab tab, FileEditorDocumentReference reference) =>
        tab.RootIndex == reference.RootIndex
        && string.Equals(tab.WorkspaceUuid, reference.WorkspaceUuid, StringComparison.Ordinal)
        && string.Equals(tab.RelativePath, reference.RelativePath, StringComparison.Ordinal);

    private static bool IsConfiguredRoot(Workspace workspace, int rootIndex)
    {
        var roots = ReadConfiguredRoots(workspace.Directories);
        return rootIndex >= 0 && rootIndex < roots.Count && !string.IsNullOrWhiteSpace(roots[rootIndex]);
    }

    private static int PathDepth(string relativePath) =>
        string.IsNullOrEmpty(relativePath)
            ? 0
            : relativePath.Count(character => character is '/' or '\\');

    private void RaiseNavigationStateChanged()
    {
        if (!_isRestoringNavigation)
        {
            NavigationStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
