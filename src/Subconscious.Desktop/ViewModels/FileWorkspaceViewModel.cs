using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>
/// Engine-only workspace file browser with independent, persistent in-memory state per open tab.
/// The Desktop process never resolves, enumerates, reads, creates, or writes workspace paths.
/// </summary>
public sealed partial class FileWorkspaceViewModel : ViewModelBase
{
    private readonly EngineClient _client = new();
    private Workspace? _workspace;
    private int _previewVersion;
    private FileEditorTab? _observedTab;

    public ObservableCollection<FileTreeNode> VisibleNodes { get; } = [];
    public ObservableCollection<FileEditorTab> OpenFiles { get; } = [];
    public ObservableCollection<WorkspaceFileRootOption> CreateRootOptions { get; } = [];

    [ObservableProperty] private string _workspaceName = "No workspace selected";
    [ObservableProperty] private string _workspaceSummary = "Select a workspace in Threads to browse its allowed folders.";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private FileEditorTab? _selectedTab;
    [ObservableProperty] private string _previewContent = string.Empty;
    [ObservableProperty] private bool _isCreatingFile;
    [ObservableProperty] private string _newFilePath = "new-note.md";
    [ObservableProperty] private WorkspaceFileRootOption? _selectedCreateRoot;

    public bool HasWorkspace => _workspace is not null;
    public bool IsFileOpen => SelectedTab is not null;
    public bool IsMarkdownFile => SelectedTab?.IsMarkdown == true;
    public bool IsDirty => SelectedTab?.IsDirty == true;
    public string Title => SelectedTab?.DisplayName ?? "Files";
    public string LineCount => $"{Math.Max(1, Content.Count(character => character == '\n') + 1)} lines";

    public string Content
    {
        get => SelectedTab?.Content ?? string.Empty;
        set
        {
            if (SelectedTab is not { } tab || tab.Content == value)
            {
                return;
            }

            tab.Content = value;
            tab.IsDirty = true;
            NotifyActiveFilePropertiesChanged();
            SchedulePreview(value);
        }
    }

    public async Task LoadWorkspaceAsync(Workspace? workspace)
    {
        _workspace = workspace;
        WorkspaceName = workspace?.Name ?? "No workspace selected";
        RebuildCreateRoots(workspace?.Directories);
        var rootCount = CreateRootOptions.Count;
        WorkspaceSummary = workspace is null
            ? "Select a workspace in Threads to browse its allowed folders."
            : rootCount == 0
                ? "This workspace has no allowed folders. Add one in Workspace settings."
                : $"{rootCount} allowed folder{(rootCount == 1 ? string.Empty : "s")}";

        VisibleNodes.Clear();
        ErrorText = null;
        if (workspace is null)
        {
            CancelNewFile();
            NotifyActiveFilePropertiesChanged();
            return;
        }

        foreach (var root in CreateRootNodes(workspace.Directories))
        {
            VisibleNodes.Add(root);
        }
        NotifyActiveFilePropertiesChanged();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadWorkspaceAsync(_workspace);

    [RelayCommand]
    private void SelectFile(FileEditorTab? tab)
    {
        if (tab is not null)
        {
            SelectedTab = tab;
        }
    }

    partial void OnSelectedTabChanged(FileEditorTab? oldValue, FileEditorTab? newValue)
    {
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged -= OnSelectedTabPropertyChanged;
        }
        _observedTab = newValue;
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged += OnSelectedTabPropertyChanged;
        }
        foreach (var tab in OpenFiles)
        {
            tab.IsSelected = ReferenceEquals(tab, newValue);
        }
        PreviewContent = newValue?.IsMarkdown == true ? newValue.Content : string.Empty;
        NotifyActiveFilePropertiesChanged();
    }

    private void OnSelectedTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(FileEditorTab.Content) or nameof(FileEditorTab.IsDirty))
        {
            NotifyActiveFilePropertiesChanged();
            if (args.PropertyName == nameof(FileEditorTab.Content) && SelectedTab?.IsMarkdown == true)
            {
                SchedulePreview(SelectedTab.Content);
            }
        }
    }

    [RelayCommand]
    private async Task ActivateNodeAsync(FileTreeNode? node)
    {
        if (node is null)
        {
            return;
        }
        if (node.IsDirectory)
        {
            await ToggleDirectoryAsync(node);
            return;
        }
        await OpenFileAsync(node);
    }

    private async Task ToggleDirectoryAsync(FileTreeNode node)
    {
        ErrorText = null;
        if (node.IsExpanded)
        {
            Collapse(node);
            return;
        }

        if (!node.ChildrenLoaded)
        {
            var workspace = _workspace;
            if (workspace is null)
            {
                return;
            }

            IsLoading = true;
            try
            {
                await EnsureRestClientAsync();
                var entries = await _client.ListWorkspaceFilesAsync(
                    workspace.Uuid,
                    node.RootIndex,
                    string.IsNullOrEmpty(node.RelativePath) ? null : node.RelativePath);
                if (_workspace?.Uuid != workspace.Uuid)
                {
                    return;
                }

                node.Children.Clear();
                foreach (var entry in entries)
                {
                    node.Children.Add(new FileTreeNode(
                        node.RootIndex, entry.RelativePath, entry.Name, entry.IsDirectory, node.Depth + 1));
                }
                node.ChildrenLoaded = true;
            }
            catch (Exception exception)
            {
                ErrorText = $"Couldn't load {node.DisplayName}: {exception.Message}";
                return;
            }
            finally
            {
                IsLoading = false;
            }
        }

        RestoreVisibleChildren(node);
        node.IsExpanded = true;
    }

    private async Task OpenFileAsync(FileTreeNode node)
    {
        var workspace = _workspace;
        if (workspace is null)
        {
            ErrorText = "Select a workspace before opening a file.";
            return;
        }

        var existing = OpenFiles.FirstOrDefault(tab => tab.WorkspaceUuid == workspace.Uuid
            && tab.RootIndex == node.RootIndex && tab.RelativePath == node.RelativePath);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        IsLoading = true;
        try
        {
            await EnsureRestClientAsync();
            var file = await _client.ReadWorkspaceFileAsync(workspace.Uuid, node.RootIndex, node.RelativePath);
            var tab = new FileEditorTab(workspace.Uuid, node.RootIndex, node.RelativePath, node.DisplayName, file.Content);
            OpenFiles.Add(tab);
            SelectedTab = tab;
            ErrorText = null;
        }
        catch (Exception exception)
        {
            ErrorText = $"Couldn't open {node.DisplayName}: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void StartNewFile()
    {
        if (_workspace is null || CreateRootOptions.Count == 0)
        {
            ErrorText = "Add an allowed workspace folder before creating a file.";
            return;
        }

        SelectedCreateRoot ??= CreateRootOptions[0];
        NewFilePath = "new-note.md";
        IsCreatingFile = true;
        ErrorText = null;
    }

    [RelayCommand]
    private void CancelNewFile()
    {
        IsCreatingFile = false;
        NewFilePath = "new-note.md";
    }

    [RelayCommand]
    private async Task CreateNewFileAsync()
    {
        var workspace = _workspace;
        var root = SelectedCreateRoot;
        var relativePath = NewFilePath.Trim();
        if (workspace is null || root is null)
        {
            ErrorText = "Select a workspace folder before creating a file.";
            return;
        }
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            ErrorText = "Enter a file name relative to the selected workspace folder.";
            return;
        }

        IsLoading = true;
        try
        {
            await EnsureRestClientAsync();
            var file = await _client.CreateWorkspaceFileAsync(workspace.Uuid, root.RootIndex, relativePath, string.Empty);
            var tab = new FileEditorTab(workspace.Uuid, root.RootIndex, relativePath, Path.GetFileName(relativePath), file.Content);
            OpenFiles.Add(tab);
            SelectedTab = tab;
            IsCreatingFile = false;
            ErrorText = null;
            await LoadWorkspaceAsync(workspace);
        }
        catch (Exception exception)
        {
            ErrorText = $"Couldn't create {relativePath}: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var workspace = _workspace;
        var tab = SelectedTab;
        if (workspace is null || tab is null || tab.WorkspaceUuid != workspace.Uuid)
        {
            ErrorText = "Return to this file's workspace before saving it.";
            return;
        }

        IsLoading = true;
        try
        {
            await EnsureRestClientAsync();
            await _client.WriteWorkspaceFileAsync(workspace.Uuid, tab.RootIndex, tab.RelativePath, tab.Content);
            tab.IsDirty = false;
            ErrorText = null;
            NotifyActiveFilePropertiesChanged();
        }
        catch (Exception exception)
        {
            ErrorText = $"Couldn't save {tab.DisplayName}: {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSave() => SelectedTab is { IsDirty: true } tab && _workspace?.Uuid == tab.WorkspaceUuid;

    private async Task EnsureRestClientAsync()
    {
        if (!_client.IsRestConnected)
        {
            await _client.ConnectRestAsync(MauiProgram.DevMode);
        }
    }

    private void NotifyActiveFilePropertiesChanged()
    {
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(IsFileOpen));
        OnPropertyChanged(nameof(IsMarkdownFile));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LineCount));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void SchedulePreview(string content)
    {
        if (!IsMarkdownFile)
        {
            return;
        }

        var version = ++_previewVersion;
        _ = Task.Run(async () =>
        {
            await Task.Delay(175);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (version == _previewVersion && IsMarkdownFile)
                {
                    PreviewContent = content;
                }
            });
        });
    }

    private static IReadOnlyList<FileTreeNode> CreateRootNodes(string? rawDirectories) => ReadConfiguredRoots(rawDirectories)
        .Select((root, index) => (Root: root, Index: index))
        .Where(item => !string.IsNullOrWhiteSpace(item.Root))
        .Select(item => new FileTreeNode(item.Index, string.Empty, RootDisplayName(item.Root!, item.Index), isDirectory: true, depth: 0))
        .ToList();

    private void RebuildCreateRoots(string? rawDirectories)
    {
        CreateRootOptions.Clear();
        foreach (var (root, index) in ReadConfiguredRoots(rawDirectories).Select((root, index) => (root, index)))
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                CreateRootOptions.Add(new WorkspaceFileRootOption(index, RootDisplayName(root, index)));
            }
        }
        SelectedCreateRoot = CreateRootOptions.FirstOrDefault();
    }

    private void Collapse(FileTreeNode node)
    {
        var index = VisibleNodes.IndexOf(node) + 1;
        while (index < VisibleNodes.Count && VisibleNodes[index].Depth > node.Depth)
        {
            VisibleNodes.RemoveAt(index);
        }
        node.IsExpanded = false;
    }

    private void RestoreVisibleChildren(FileTreeNode node)
    {
        var index = VisibleNodes.IndexOf(node) + 1;
        InsertVisibleChildren(node, ref index);
    }

    private void InsertVisibleChildren(FileTreeNode parent, ref int index)
    {
        foreach (var child in parent.Children)
        {
            VisibleNodes.Insert(index++, child);
            if (child.IsDirectory && child.IsExpanded)
            {
                InsertVisibleChildren(child, ref index);
            }
        }
    }

    private static List<string?> ReadConfiguredRoots(string? rawDirectories)
    {
        if (string.IsNullOrWhiteSpace(rawDirectories))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<string?>>(rawDirectories) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string RootDisplayName(string root, int index)
    {
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? $"Folder {index + 1}" : name;
    }
}
