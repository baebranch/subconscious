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
    private bool _connected;

    public ObservableCollection<Workspace> Workspaces { get; } = [];

    public bool IsLoading { get; private set; }

    public string? ErrorMessage { get; private set; }

    public WorkspaceStore(EngineClient client)
    {
        _client = client;
    }

    /// <summary>Connects to the engine if needed and (re)loads the workspace list. Safe to call
    /// repeatedly — e.g. once at app startup and again every time the Workspaces page appears —
    /// since it's just "connect if not already connected, then refresh".</summary>
    public async Task RefreshAsync(bool dev = false)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            if (!_connected)
            {
                await _client.ConnectAsync(dev);
                _connected = true;
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
            // Swallow rather than throw: the startup caller fires this without awaiting, and
            // WorkspacesPage surfaces ErrorMessage in its UI instead of crashing on a missing
            // engine (expected on a fresh Android/iOS install with no paired engine yet).
            _connected = false;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
