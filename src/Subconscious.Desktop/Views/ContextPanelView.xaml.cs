using Subconscious.Desktop.Engine;
using Subconscious.Desktop.ViewModels;

namespace Subconscious.Desktop.Views;

/// <summary>The right-hand context panel (Threads / Workspaces / Settings / Account). Inherits
/// MainPage's BindingContext (<c>MainViewModel</c>).</summary>
public partial class ContextPanelView : ContentView
{
    public ContextPanelView()
    {
        InitializeComponent();
    }

    /// <summary>Opens the selected workspace's settings form. Changing the chat's active
    /// workspace is deliberately handled only by the Threads header dropdown.</summary>
    private void OnWorkspaceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Workspace workspace)
        {
            return;
        }

        WorkspacesView.SelectedItem = null;

        if (BindingContext is MainViewModel viewModel)
        {
            viewModel.EditWorkspaceCommand.Execute(workspace);
        }
    }

    /// <summary>Loads the workspace selected from the Threads header's native dropdown, or clears
    /// its filter when the explicit All workspaces item is selected.</summary>
    private async void OnWorkspacePickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (sender is not Picker { SelectedItem: WorkspaceSelectorItem selected }
            || BindingContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            if (selected.Workspace is null)
            {
                if (viewModel.Chat.CurrentWorkspace is not null)
                {
                    await viewModel.Chat.ClearWorkspaceSelectionAsync();
                }
                return;
            }

            if (selected.Workspace.Uuid != viewModel.Chat.CurrentWorkspace?.Uuid)
            {
                await viewModel.Chat.SelectWorkspaceAsync(selected.Workspace);
            }
        }
        catch (Exception ex)
        {
            viewModel.Chat.StatusText = $"Couldn't open workspace: {ex.Message}";
            if (sender is Picker picker)
            {
                picker.SelectedItem = viewModel.Chat.CurrentWorkspaceSelector;
            }
        }
    }


}
