using CommunityToolkit.Mvvm.Input;

namespace Subconscious.Desktop.ViewModels;

public sealed partial class FileWorkspaceViewModel
{
    /// <summary>Opens non-persistent fixture tabs so every editor mode can be exercised without a workspace.</summary>
    [RelayCommand]
    private void OpenSamples()
    {
        FileEditorTab? firstNewTab = null;
        foreach (var sample in FileEditorSamples.CreateTabs())
        {
            var existing = OpenFiles.FirstOrDefault(tab => tab.WorkspaceUuid == sample.WorkspaceUuid
                && tab.RelativePath == sample.RelativePath);
            if (existing is null)
            {
                OpenFiles.Add(sample);
                firstNewTab ??= sample;
            }
            else
            {
                firstNewTab ??= existing;
            }
        }

        SelectedTab = firstNewTab;
        ErrorText = null;
    }

    private FileEditorTab? _pendingDirtyClose;

    /// <summary>Closes clean tabs immediately; a second close click confirms discarding a dirty tab.</summary>
    [RelayCommand]
    private void CloseFile(FileEditorTab? tab)
    {
        if (tab is null)
        {
            return;
        }
        if (tab.IsDirty && !ReferenceEquals(_pendingDirtyClose, tab))
        {
            _pendingDirtyClose = tab;
            ErrorText = $"{tab.DisplayName} has unsaved changes. Click its close button again to discard them.";
            return;
        }

        var index = OpenFiles.IndexOf(tab);
        OpenFiles.Remove(tab);
        _pendingDirtyClose = null;
        ErrorText = null;
        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = OpenFiles.Count == 0 ? null : OpenFiles[Math.Min(index, OpenFiles.Count - 1)];
        }
    }
}
