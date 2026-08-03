using Subconscious.Mobile.Engine;

namespace Subconscious.Mobile;

/// <summary>
/// Lists the workspaces known to the local Subconscious engine. Reached via the flyout's
/// "Workspaces" item (see <c>AppShell</c>). Resolved by Shell through a parameterless
/// <c>DataTemplate</c> rather than constructor injection — <see cref="Shell.FlyoutContent"/>'s
/// custom layout only registers routes through plain <c>ShellContent</c> declarations, which
/// don't support DI-constructed pages — so this pulls <see cref="WorkspaceStore"/> from the
/// current <see cref="IPlatformApplication"/>'s service provider instead.
/// </summary>
public partial class WorkspacesPage : ContentPage
{
	private readonly WorkspaceStore _store;

	public WorkspacesPage()
	{
		InitializeComponent();

		_store = IPlatformApplication.Current?.Services.GetService<WorkspaceStore>()
			?? throw new InvalidOperationException("WorkspaceStore is not registered.");

		WorkspacesView.ItemsSource = _store.Workspaces;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await RefreshAsync();
	}

	private async void OnRefreshing(object? sender, EventArgs e)
	{
		await RefreshAsync();
	}

	private async Task RefreshAsync()
	{
		await _store.RefreshAsync();

		ErrorBanner.IsVisible = _store.ErrorMessage is not null;
		ErrorLabel.Text = _store.ErrorMessage;

		WorkspacesRefreshView.IsRefreshing = false;
	}
}
