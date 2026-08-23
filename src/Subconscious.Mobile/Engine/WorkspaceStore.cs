using System.Collections.ObjectModel;

namespace Subconscious.Mobile.Engine;

/// <summary>
/// App-wide holder for the workspace list, backed by <see cref="EngineClient"/>. Registered as
/// a singleton (see <c>MauiProgram</c>) so the same <see cref="Workspaces"/> collection is
/// shared between whatever kicks off the initial load (currently <c>AppShell</c>'s constructor)
/// and <c>WorkspacesPage</c>, which just displays whatever is already there and refreshes on
/// appearing rather than owning its own connection.
/// </summary>
public sealed class WorkspaceStore
{
    private readonly EngineClient _client;

    public ObservableCollection<Workspace> Workspaces { get; } = [];

    public bool IsLoading { get; private set; }

    public string? ErrorMessage { get; private set; }

    public WorkspaceStore(EngineClient client)
    {
        _client = client;
    }

    public void Replace(Workspace workspace)
    {
        var index = Workspaces.ToList().FindIndex(candidate => candidate.Uuid == workspace.Uuid);
        if (index >= 0)
        {
            Workspaces[index] = workspace;
        }
        else
        {
            Workspaces.Add(workspace);
        }
    }

    /// <summary>Refreshes the shared list through an already-established Engine client.
    /// Exceptions are recorded for UI callers rather than crashing an optional refresh gesture.</summary>
    public Task RefreshAsync() => RefreshCoreAsync(throwOnFailure: false);

    /// <summary>Refreshes after the session has explicitly selected and connected an Engine endpoint.
    /// This never runs discovery, so Android retains its active local-development or paired-LAN connection.</summary>
    public Task RefreshConnectedAsync() => RefreshCoreAsync(throwOnFailure: true);

    private async Task RefreshCoreAsync(bool throwOnFailure)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            if (!_client.IsRestConnected)
            {
                throw new InvalidOperationException("No Subconscious engine is connected.");
            }

            var workspaces = await _client.ListWorkspacesAsync();
            Workspaces.Clear();
            foreach (var workspace in workspaces)
            {
                Workspaces.Add(workspace);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            if (throwOnFailure)
            {
                throw;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
