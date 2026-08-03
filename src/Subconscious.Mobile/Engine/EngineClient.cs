using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Subconscious.Mobile.Engine;

/// <summary>
/// REST client for the local Subconscious engine API, scoped to what the Mobile app needs
/// today (workspace listing/CRUD). A trimmed-down structural port of
/// <c>Subconscious.Desktop.Engine.EngineClient</c> — the WebSocket-based chat streaming half
/// isn't ported yet since Mobile's chat page is still the dummy echo slice; add it here
/// alongside <c>MainPage</c> wiring up to the real engine.
/// </summary>
public sealed class EngineClient : IAsyncDisposable
{
    private RuntimeInfo? _info;
    private HttpClient? _http;

    public bool IsConnected => _http is not null;

    public async Task ConnectAsync(bool dev)
    {
        _info = await EngineDiscovery.DiscoverAsync(dev);

        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{_info.Host}:{_info.Port}/api/v1/"),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _info.Token);
    }

    public async Task<List<Workspace>> ListWorkspacesAsync() =>
        await Http.GetFromJsonAsync<List<Workspace>>("workspaces") ?? [];

    public async Task<Workspace> CreateWorkspaceAsync(string name, string? description = null, string? defaultModelId = null) =>
        await (await Http.PostAsJsonAsync("workspaces", new CreateWorkspaceRequest { Name = name, Description = description, DefaultModelId = defaultModelId }))
            .Content.ReadFromJsonAsync<Workspace>() ?? throw new InvalidOperationException("Empty response creating workspace.");

    public async Task<List<ThreadInfo>> ListThreadsAsync(string workspaceUuid) =>
        await Http.GetFromJsonAsync<List<ThreadInfo>>($"workspaces/{workspaceUuid}/threads") ?? [];

    private HttpClient Http => _http ?? throw new InvalidOperationException("Not connected.");

    public ValueTask DisposeAsync()
    {
        _http?.Dispose();
        return ValueTask.CompletedTask;
    }
}
