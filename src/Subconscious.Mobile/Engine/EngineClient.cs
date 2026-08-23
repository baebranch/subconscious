using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Subconscious.Mobile.Engine;

public sealed record ChatDeltaEventArgs(string? TurnId, string ThreadUuid, string Delta);
public sealed record ChatDoneEventArgs(string? TurnId, string ThreadUuid);
public sealed record ChatErrorEventArgs(string? TurnId, string ThreadUuid, string Error);
public sealed record ChatCancelledEventArgs(string? TurnId, string ThreadUuid);
public sealed record ToolApprovalRequestEventArgs(string TurnId, string ApprovalId, string ThreadUuid, string ToolName, string Arguments, string Operation);

/// <summary>Authenticated REST and event-stream client for a paired Subconscious engine.</summary>
public sealed class EngineClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private HttpClient? _http;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCancellation;
    private EngineEndpoint? _endpoint;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<ChatDeltaEventArgs>? ChatDelta;
    public event EventHandler<ChatDoneEventArgs>? ChatDone;
    public event EventHandler<ChatErrorEventArgs>? ChatError;
    public event EventHandler<ChatCancelledEventArgs>? ChatCancelled;
    public event EventHandler<ToolApprovalRequestEventArgs>? ToolApprovalRequested;

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public bool IsRestConnected => _http is not null;

    public async Task ConnectAsync(bool dev)
    {
        var runtimeInfo = await EngineDiscovery.DiscoverAsync(dev);
        await ConnectAsync(new EngineEndpoint(runtimeInfo.Host, runtimeInfo.Port, runtimeInfo.Token, runtimeInfo.NodeId));
    }

    /// <summary>Connects to a user-paired LAN engine without relying on local runtime.json discovery.</summary>
    public async Task ConnectAsync(EngineEndpoint endpoint)
    {
        await _connectionGate.WaitAsync();
        try
        {
            if (_http is not null && !Equals(_endpoint, endpoint))
            {
                await DisconnectCoreAsync();
            }

            if (_http is null)
            {
                _endpoint = endpoint;
                _http = CreateHttpClient(endpoint);
                await OpenSocketAsync(endpoint);
            }
            else if (_socket is not { State: WebSocketState.Open })
            {
                await OpenSocketAsync(endpoint);
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionGate.WaitAsync();
        try { await DisconnectCoreAsync(); }
        finally { _connectionGate.Release(); }
    }

    private static HttpClient CreateHttpClient(EngineEndpoint endpoint)
    {
        var client = new HttpClient { BaseAddress = CreateUri(Uri.UriSchemeHttp, endpoint, "/api/v1/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
        return client;
    }

    private async Task OpenSocketAsync(EngineEndpoint endpoint)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {endpoint.Token}");
        try
        {
            await socket.ConnectAsync(CreateUri("ws", endpoint, "/api/v1/events"), CancellationToken.None);
            var previousSocket = Interlocked.Exchange(ref _socket, socket);
            var previousCancellation = Interlocked.Exchange(ref _receiveCancellation, new CancellationTokenSource());
            previousCancellation?.Cancel();
            previousSocket?.Dispose();
            await SendFrameAsync("client.hello", new { });
            _ = ReceiveLoopAsync(socket, _receiveCancellation!.Token);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                HandleFrame(Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (WebSocketException) { }
        finally
        {
            if (ReferenceEquals(_socket, socket)) ConnectionStatusChanged?.Invoke(this, false);
        }
    }

    private void HandleFrame(string json)
    {
        WsFrame? frame;
        try { frame = JsonSerializer.Deserialize<WsFrame>(json, JsonOptions); }
        catch (JsonException) { return; }
        if (frame is null) return;
        var data = frame.Data;
        switch (frame.Type)
        {
            case "client.hello.ack": ConnectionStatusChanged?.Invoke(this, true); break;
            case "chat.delta" when data is { } delta:
                ChatDelta?.Invoke(this, new(frame.Id, String(delta, "thread_uuid"), String(delta, "delta"))); break;
            case "chat.done" when data is { } done:
                ChatDone?.Invoke(this, new(frame.Id, String(done, "thread_uuid"))); break;
            case "chat.cancelled" when data is { } cancelled:
                ChatCancelled?.Invoke(this, new(frame.Id, String(cancelled, "thread_uuid"))); break;
            case "chat.error" when data is { } error:
                ChatError?.Invoke(this, new(frame.Id, String(error, "thread_uuid"), String(error, "error", "Chat failed."))); break;
            case "tool.approval.request" when data is { } approval && frame.Id is { Length: > 0 } turnId && String(approval, "approval_id") is { Length: > 0 } approvalId:
                ToolApprovalRequested?.Invoke(this, new(turnId, approvalId, String(approval, "thread_uuid"), String(approval, "tool_name", "tool"), approval.TryGetProperty("arguments", out var args) ? args.GetRawText() : "{}", String(approval, "operation", "mutation"))); break;
        }
    }

    private static string String(JsonElement data, string property, string fallback = "") =>
        data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    public string SendChat(string? threadUuid, string content, string? workspaceUuid, string? modelId)
    {
        var turnId = Guid.NewGuid().ToString("N");
        _ = SendFrameAsync("chat.send", new { thread_uuid = threadUuid, workspace_uuid = workspaceUuid, content, model_id = modelId }, turnId);
        return turnId;
    }

    public void CancelChat(string? turnId) => _ = SendFrameAsync("chat.cancel", new { turn_id = turnId }, turnId);
    public void ResolveToolApproval(string turnId, string approvalId, bool approve) => _ = SendFrameAsync("tool.approval.response", new { approval_id = approvalId, decision = approve ? "approve" : "deny" }, turnId);

    private Task SendFrameAsync<T>(string type, T data, string? id = null)
    {
        if (_socket is not { State: WebSocketState.Open } socket) return Task.CompletedTask;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { v = 1, type, id, data }));
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public Task<List<Workspace>> ListWorkspacesAsync() => GetListAsync<Workspace>("workspaces");
    public Task<List<ThreadInfo>> ListThreadsAsync(string workspaceUuid) => GetListAsync<ThreadInfo>($"workspaces/{Uri.EscapeDataString(workspaceUuid)}/threads");
    public Task<List<ChatMessage>> ListMessagesAsync(string threadUuid) => GetListAsync<ChatMessage>($"threads/{Uri.EscapeDataString(threadUuid)}/messages");
    public Task<List<ModelInfo>> ListModelsAsync() => GetListAsync<ModelInfo>("models");
    public Task<Workspace> CreateWorkspaceAsync(string name, string? description = null, string? defaultModelId = null) => PostAsync<Workspace, CreateWorkspaceRequest>("workspaces", new() { Name = name, Description = description, DefaultModelId = defaultModelId });
    public Task<Workspace> UpdateWorkspaceAsync(string uuid, CreateWorkspaceRequest request) =>
        PutAsync<Workspace, CreateWorkspaceRequest>($"workspaces/{Uri.EscapeDataString(uuid)}", request);
    public Task<ThreadInfo> UpdateThreadModelAsync(string threadUuid, string modelId) => PutAsync<ThreadInfo, UpdateThreadRequest>($"threads/{Uri.EscapeDataString(threadUuid)}", new() { DefaultModelId = modelId });

    private async Task<List<T>> GetListAsync<T>(string path)
    {
        using var response = await Http.GetAsync(path);
        await EnsureSuccessAsync(response, HttpMethod.Get, path);
        return await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions) ?? [];
    }

    private async Task<TResult> PostAsync<TResult, TBody>(string path, TBody body)
    {
        using var response = await Http.PostAsJsonAsync(path, body);
        await EnsureSuccessAsync(response, HttpMethod.Post, path);
        return await response.Content.ReadFromJsonAsync<TResult>(JsonOptions) ?? throw new InvalidOperationException($"{path} returned no content.");
    }

    private async Task<TResult> PutAsync<TResult, TBody>(string path, TBody body)
    {
        using var response = await Http.PutAsJsonAsync(path, body);
        await EnsureSuccessAsync(response, HttpMethod.Put, path);
        return await response.Content.ReadFromJsonAsync<TResult>(JsonOptions) ?? throw new InvalidOperationException($"{path} returned no content.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, HttpMethod method, string path)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"{method} {path} failed: {(int)response.StatusCode} {detail[..Math.Min(detail.Length, 300)]}", null, response.StatusCode);
    }

    private HttpClient Http => _http ?? throw new InvalidOperationException("No paired Subconscious engine is connected.");

    private static Uri CreateUri(string scheme, EngineEndpoint endpoint, string path) =>
        new UriBuilder(scheme, endpoint.Host, endpoint.Port, path).Uri;

    private async Task DisconnectCoreAsync()
    {
        var cancellation = Interlocked.Exchange(ref _receiveCancellation, null);
        cancellation?.Cancel();
        var socket = Interlocked.Exchange(ref _socket, null);
        if (socket is { State: WebSocketState.Open })
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
        }
        socket?.Dispose();
        cancellation?.Dispose();
        Interlocked.Exchange(ref _http, null)?.Dispose();
        _endpoint = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _connectionGate.Dispose();
    }
}
