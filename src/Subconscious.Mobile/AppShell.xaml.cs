using Subconscious.Mobile.Engine;

namespace Subconscious.Mobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Kick off the workspace list load as soon as the shell exists, so it's already
		// populated (or has failed with ErrorMessage set) by the time the user opens the
		// Workspaces page — WorkspacesPage's own OnAppearing refresh then just picks up
		// whatever WorkspaceStore already has and re-syncs. Fire-and-forget is intentional:
		// there's no UI to report progress against yet, and WorkspaceStore swallows failures
		// (e.g. "no engine running") into ErrorMessage rather than throwing.
		var store = IPlatformApplication.Current?.Services.GetService<WorkspaceStore>();
		if (store is not null)
		{
			_ = store.RefreshAsync();
		}
	}

	// Shell.FlyoutContent replaces the entire built-in flyout list. Since MAUI's
	// default flyout-closing behavior only applies to its auto-rendered items, we
	// need to explicitly close the flyout after each tap by setting
	// FlyoutIsPresented = false.
	private async void OnThreadsTapped(object? sender, TappedEventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await Shell.Current.GoToAsync("//ThreadsPage");
	}

	private async void OnWorkspacesTapped(object? sender, TappedEventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await Shell.Current.GoToAsync("//WorkspacesPage");
	}

	private async void OnThreadsListTapped(object? sender, TappedEventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await Shell.Current.GoToAsync("//ThreadsListPage");
	}

	private async void OnSettingsTapped(object? sender, TappedEventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await Shell.Current.GoToAsync("//SettingsPage");
	}

	private async void OnAccountTapped(object? sender, TappedEventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await Shell.Current.GoToAsync("//AccountPage");
	}
}
