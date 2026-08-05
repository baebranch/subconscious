using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Subconscious.Desktop.Engine;

namespace Subconscious.Desktop.ViewModels;

/// <summary>
/// Drives the center-panel form shown when creating or editing a workspace from the
/// Workspaces context-panel section. Fields mirror the editable attributes of the engine's
/// <c>Workspace</c> model (<c>Name</c>, <c>Description</c>, <c>DefaultModelId</c>) — <c>Uuid</c>,
/// <c>CreatedAt</c> and <c>UpdatedAt</c> are shown read-only in edit mode. Backed by
/// <see cref="ChatViewModel.CreateWorkspaceEntryAsync"/>/<see cref="ChatViewModel.UpdateWorkspaceEntryAsync"/>
/// so the Workspaces list stays in sync with whatever the form persists.
/// </summary>
public sealed partial class WorkspaceFormViewModel : ViewModelBase
{
    private readonly ChatViewModel _chat;

    /// <summary>Null in create mode; the workspace's UUID in edit mode.</summary>
    public string? Uuid { get; }

    /// <summary>Database identifier used by the fixture-compatible UI state.</summary>
    public int? Id { get; }

    public bool IsEditMode => Uuid is not null;

    public DateTime? CreatedAt { get; }
    public DateTime? UpdatedAt { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _defaultModelId;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isSaving;

    /// <summary>Raised after a successful save so the host can close the form / refresh selection.</summary>
    public event EventHandler<Workspace>? Saved;

    /// <summary>Raised when the user cancels out of the form without saving.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Create-mode constructor: an empty form for a brand-new workspace.</summary>
    public WorkspaceFormViewModel(ChatViewModel chat)
    {
        _chat = chat;
        Name = string.Empty;
    }

    /// <summary>Edit-mode constructor: pre-filled from an existing workspace.</summary>
    public WorkspaceFormViewModel(ChatViewModel chat, Workspace workspace)
    {
        _chat = chat;
        Uuid = workspace.Uuid;
        Id = workspace.Id;
        CreatedAt = workspace.CreatedAt;
        UpdatedAt = workspace.UpdatedAt;
        Name = workspace.Name;
        Description = workspace.Description;
        DefaultModelId = workspace.DefaultModelId;
    }

    private bool CanSave => !IsSaving && Name.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            ErrorText = "Name is required.";
            return;
        }

        IsSaving = true;
        ErrorText = null;
        try
        {
            var description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            var defaultModelId = string.IsNullOrWhiteSpace(DefaultModelId) ? null : DefaultModelId.Trim();

            var workspace = IsEditMode
                ? await _chat.UpdateWorkspaceEntryAsync(Uuid!, name, description, defaultModelId)
                : await _chat.CreateWorkspaceEntryAsync(name, description, defaultModelId);

            Saved?.Invoke(this, workspace);
        }
        catch (Exception ex)
        {
            ErrorText = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    partial void OnNameChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
