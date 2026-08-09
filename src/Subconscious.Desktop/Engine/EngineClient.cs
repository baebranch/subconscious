using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Subconscious.Desktop.Engine;

/// <summary>
/// Delta of a single streaming chat token, raised as the corresponding
/// <c>chat.delta</c> frame arrives.
/// </summary>
public sealed record ChatDeltaEventArgs(string? TurnId, string ThreadUuid, string Delta);

/// <summary>Raised on <c>chat.done</c> — the turn's assistant message has finished streaming.</summary>
public sealed record ChatDoneEventArgs(string? TurnId, string ThreadUuid);

/// <summary>Raised on <c>chat.error</c> — the turn failed; whatever streamed so far is discarded by the caller's UI.</summary>
public sealed record ChatErrorEventArgs(string? TurnId, string ThreadUuid, string Error);

/// <summary>Raised when the engine acknowledges cancellation of a running turn.</summary>
public sealed record ChatCancelledEventArgs(string? TurnId, string ThreadUuid);

/// <summary>Raised when the selected workspace/thread policy requires a tool-call decision.</summary>
public sealed record ToolApprovalRequestEventArgs(
    string TurnId,
    string ApprovalId,
    string ThreadUuid,
    string ToolName,
    string Arguments,
    string Operation);

/// <summary>
/// A REST call reached the engine and the engine said no.
///
/// Worth having as its own type because the alternative is what this client used to do: hand a
/// non-2xx response straight to <c>ReadFromJsonAsync</c>, which fails somewhere down in the
/// deserializer with a message about JSON tokens and no mention of the status code. A 500 from a
/// broken query and a 401 from a stale token then look identical, and both look like a bug in the
/// client rather than an answer from the server.
/// </summary>
public sealed class EngineApiException : Exception
{
    /// <summary>How much of the response body to keep. Enough to read an error payload, short
    /// enough that an HTML error page doesn't end up in a UI label.</summary>
    private const int MaxBodyLength = 300;

    public EngineApiException(HttpMethod method, string path, HttpStatusCode statusCode, string? body)
        : base(Describe(method, path, statusCode, body))
    {
        Method = method;
        Path = path;
        StatusCode = statusCode;
        ResponseBody = body;
    }

    public HttpMethod Method { get; }

    /// <summary>Path relative to the engine's <c>/api/v1/</c> base, e.g. <c>workspaces</c>.</summary>
    public string Path { get; }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }

    private static string Describe(HttpMethod method, string path, HttpStatusCode statusCode, string? body)
    {
        var message = $"{method} {path} failed: {(int)statusCode} {statusCode}";
        if (string.IsNullOrWhiteSpace(body))
        {
            return message;
        }

        var trimmed = body.Trim();
        if (trimmed.Length > MaxBodyLength)
        {
            trimmed = trimmed[..MaxBodyLength] + "…";
        }
        return $"{message} — {trimmed}";
    }
}

/// <summary>
/// Client for the local Subconscious engine API — REST for discrete reads/commands, one
/// WebSocket for live events and streaming chat. Direct structural port of
/// <c>subconscious-code/src/engine/client.ts</c>'s <c>EngineClient</c>.
/// </summary>
public sealed class EngineClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private RuntimeInfo? _info;
    private HttpClient? _http;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveLoopCts;
    private bool _closing;
    private bool _dev;
    private int _reconnectScheduled;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<ChatDeltaEventArgs>? ChatDelta;
    public event EventHandler<ChatDoneEventArgs>? ChatDone;
    public event EventHandler<ChatErrorEventArgs>? ChatError;
    public event EventHandler<ChatCancelledEventArgs>? ChatCancelled;
    public event EventHandler<ToolApprovalRequestEventArgs>? ToolApprovalRequested;
    public event EventHandler<ChatMessage>? MessageCreated;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    /// <summary>True after engine discovery created the bearer-authenticated REST client.</summary>
    public bool IsRestConnected => _http is not null;

    public async Task ConnectAsync(bool dev)
    {
        _closing = false;
        _dev = dev;
        await _connectionGate.WaitAsync();
        try
        {
            await ConnectRestCoreAsync(dev);
            await OpenSocketCoreAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>Connect only the REST client. Settings pages do not need a second WebSocket.</summary>
    public async Task ConnectRestAsync(bool dev)
    {
        _closing = false;
        _dev = dev;
        await _connectionGate.WaitAsync();
        try
        {
            await ConnectRestCoreAsync(dev);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task ConnectRestCoreAsync(bool dev)
    {
        var info = await EngineDiscovery.DiscoverAsync(dev);
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{info.Host}:{info.Port}/api/v1/"),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);

        var previous = Interlocked.Exchange(ref _http, client);
        _info = info;
        previous?.Dispose();
    }

    private async Task OpenSocketCoreAsync()
    {
        var info = _info ?? throw new InvalidOperationException("Not connected.");
        var socket = new ClientWebSocket();
        try
        {
            var uri = new Uri($"ws://{info.Host}:{info.Port}/api/v1/events?token={Uri.EscapeDataString(info.Token)}");
            await socket.ConnectAsync(uri, CancellationToken.None);

            var receiveLoopCts = new CancellationTokenSource();
            var previousSocket = Interlocked.Exchange(ref _ws, socket);
            var previousReceiveLoopCts = Interlocked.Exchange(ref _receiveLoopCts, receiveLoopCts);
            previousReceiveLoopCts?.Cancel();
            previousSocket?.Dispose();

            await SendFrameAsync("client.hello", new { });
            _ = Task.Run(() => ReceiveLoopAsync(socket, receiveLoopCts.Token));
        }
        catch
        {
            if (ReferenceEquals(_ws, socket))
            {
                Interlocked.CompareExchange(ref _ws, null, socket);
                var receiveLoopCts = Interlocked.Exchange(ref _receiveLoopCts, null);
                receiveLoopCts?.Cancel();
                receiveLoopCts?.Dispose();
            }
            socket.Dispose();
            throw;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 16];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                HandleFrame(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when a newer connection replaces this one or on DisconnectAsync.
        }
        catch (WebSocketException)
        {
            // The finally block below treats abrupt transport failures exactly like a close frame.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // The superseded socket was deliberately disposed.
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_ws, socket))
            {
                ConnectionStatusChanged?.Invoke(this, false);
                ScheduleReconnect();
            }
        }
    }

    /// <summary>
    /// Reconnect in the background until the engine is reachable again. Every retry rediscovers
    /// the engine instead of reusing the old port/token, so an Engine restart is recovered rather
    /// than treated as a permanently disconnected WebSocket.
    /// </summary>
    private void ScheduleReconnect()
    {
        if (Interlocked.CompareExchange(ref _reconnectScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_closing)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    if (_closing)
                    {
                        return;
                    }

                    try
                    {
                        await _connectionGate.WaitAsync();
                        try
                        {
                            if (_closing)
                            {
                                return;
                            }
                            await ConnectRestCoreAsync(_dev);
                            if (_closing)
                            {
                                return;
                            }
                            await OpenSocketCoreAsync();
                            return;
                        }
                        finally
                        {
                            _connectionGate.Release();
                        }
                    }
                    catch (Exception)
                    {
                        if (_closing)
                        {
                            return;
                        }

                        // The disconnected state remains visible while the local engine starts.
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectScheduled, 0);
            }
        });
    }

    private void HandleFrame(string json)
    {
        WsFrame? frame;
        try
        {
            frame = JsonSerializer.Deserialize<WsFrame>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }
        if (frame is null)
        {
            return;
        }

        switch (frame.Type)
        {
            case "client.hello.ack":
                ConnectionStatusChanged?.Invoke(this, true);
                break;
            case "chat.delta":
                if (frame.Data is { } deltaData)
                {
                    var threadUuid = GetString(deltaData, "thread_uuid") ?? string.Empty;
                    var delta = GetString(deltaData, "delta") ?? string.Empty;
                    ChatDelta?.Invoke(this, new ChatDeltaEventArgs(frame.Id, threadUuid, delta));
                }
                break;
            case "chat.done":
                if (frame.Data is { } doneData)
                {
                    var threadUuid = GetString(doneData, "thread_uuid") ?? string.Empty;
                    ChatDone?.Invoke(this, new ChatDoneEventArgs(frame.Id, threadUuid));
                }
                break;
            case "chat.cancelled":
                if (frame.Data is { } cancelledData)
                {
                    var threadUuid = GetString(cancelledData, "thread_uuid") ?? string.Empty;
                    ChatCancelled?.Invoke(this, new ChatCancelledEventArgs(frame.Id, threadUuid));
                }
                break;
            case "tool.approval.request":
                if (frame.Data is { } approvalData
                    && frame.Id is { Length: > 0 } turnId
                    && GetString(approvalData, "approval_id") is { Length: > 0 } approvalId)
                {
                    var arguments = approvalData.TryGetProperty("arguments", out var argumentsData)
                        ? argumentsData.GetRawText()
                        : "{}";
                    ToolApprovalRequested?.Invoke(this, new ToolApprovalRequestEventArgs(
                        turnId,
                        approvalId,
                        GetString(approvalData, "thread_uuid") ?? string.Empty,
                        GetString(approvalData, "tool_name") ?? "tool",
                        arguments,
                        GetString(approvalData, "operation") ?? "mutation"));
                }
                break;
            case "chat.error":
                if (frame.Data is { } errData)
                {
                    var threadUuid = GetString(errData, "thread_uuid") ?? string.Empty;
                    var error = GetString(errData, "error") ?? "chat error";
                    ChatError?.Invoke(this, new ChatErrorEventArgs(frame.Id, threadUuid, error));
                }
                break;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private Task SendFrameAsync<T>(string type, T data, string? id = null)
    {
        var socket = _ws;
        if (socket is not { State: WebSocketState.Open })
        {
            return Task.CompletedTask;
        }
        var envelope = new { v = 1, type, id, data };
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>Send a chat message. An existing thread uses <paramref name="threadUuid"/>;
    /// a local draft uses <paramref name="workspaceUuid"/>, which makes the engine create and
    /// name its thread before streaming the response. Returns the turn's correlation id.</summary>
    public string SendChat(string? threadUuid, string content, string? workspaceUuid = null, string? modelId = null)
    {
        var turnId = Guid.NewGuid().ToString("N");
        _ = SendFrameAsync("chat.send", new
        {
            thread_uuid = threadUuid,
            workspace_uuid = workspaceUuid,
            content,
            model_id = modelId,
        }, turnId);
        return turnId;
    }

    public void CancelChat(string? turnId) => _ = SendFrameAsync("chat.cancel", new { turn_id = turnId }, turnId);

    /// <summary>Resolves a user-facing approval request. The correlation ID is retained in the
    /// envelope so the engine can reject cross-turn or stale responses.</summary>
    public void ResolveToolApproval(string turnId, string approvalId, bool approve) =>
        _ = SendFrameAsync("tool.approval.response", new
        {
            approval_id = approvalId,
            decision = approve ? "approve" : "deny",
        }, turnId);

    // ── REST ──────────────────────────────────────────────────────────────────
    // Every call goes through SendAsync below, which turns a non-2xx into an
    // EngineApiException. GET /workspaces returning 500 (as it did against a database whose
    // schema predated workspaces.default_model_id) has to read as "the engine returned 500", not
    // as a deserialization failure.

    /// <summary>Every workspace known to the engine, ordered by name (<c>GET /workspaces</c>).</summary>
    public Task<List<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
        ListAsync<Workspace>("workspaces", cancellationToken);

    public Task<Workspace> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<Workspace, CreateWorkspaceRequest>(HttpMethod.Post, "workspaces", request, cancellationToken);

    public Task<Workspace> CreateWorkspaceAsync(string name, string? description = null, string? defaultModelId = null, CancellationToken cancellationToken = default) =>
        CreateWorkspaceAsync(new CreateWorkspaceRequest { Name = name, Description = description, DefaultModelId = defaultModelId }, cancellationToken);

    public Task<Workspace> UpdateWorkspaceAsync(string uuid, CreateWorkspaceRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<Workspace, CreateWorkspaceRequest>(
            HttpMethod.Put, $"workspaces/{Uri.EscapeDataString(uuid)}", request, cancellationToken);

    public Task<Workspace> UpdateWorkspaceAsync(string uuid, string name, string? description = null, string? defaultModelId = null, CancellationToken cancellationToken = default) =>
        UpdateWorkspaceAsync(uuid, new CreateWorkspaceRequest { Name = name, Description = description, DefaultModelId = defaultModelId }, cancellationToken);

    public Task<List<ThreadInfo>> ListThreadsAsync(string workspaceUuid, CancellationToken cancellationToken = default) =>
        ListAsync<ThreadInfo>($"workspaces/{Uri.EscapeDataString(workspaceUuid)}/threads", cancellationToken);

    public Task<ThreadInfo> CreateThreadAsync(string workspaceUuid, string? title = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ThreadInfo, CreateThreadRequest>(
            HttpMethod.Post,
            "threads",
            new CreateThreadRequest { WorkspaceUuid = workspaceUuid, Title = title },
            cancellationToken);

    /// <summary>Sets the explicit model override for a persisted thread.</summary>
    public Task<ThreadInfo> UpdateThreadModelAsync(string threadUuid, string modelId, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ThreadInfo, UpdateThreadRequest>(
            HttpMethod.Put,
            $"threads/{Uri.EscapeDataString(threadUuid)}",
            new UpdateThreadRequest { DefaultModelId = modelId },
            cancellationToken);

    public Task<List<ChatMessage>> ListMessagesAsync(string threadUuid, CancellationToken cancellationToken = default) =>
        ListAsync<ChatMessage>($"threads/{Uri.EscapeDataString(threadUuid)}/messages", cancellationToken);

    public Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        ListAsync<ModelInfo>("models", cancellationToken);

    /// <summary>Reads generic app-state settings, optionally scoped by key, tag, and client.</summary>
    public Task<List<AppStateSetting>> GetSettingsAsync(
        string? key = null,
        string? tag = null,
        string? client = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>();
        if (key is not null) parameters.Add($"key={Uri.EscapeDataString(key)}");
        if (tag is not null) parameters.Add($"tag={Uri.EscapeDataString(tag)}");
        if (client is not null) parameters.Add($"client={Uri.EscapeDataString(client)}");
        var path = parameters.Count == 0 ? "settings" : $"settings?{string.Join("&", parameters)}";
        return ListAsync<AppStateSetting>(path, cancellationToken);
    }

    /// <summary>Upserts a batch of generic app-state settings in one engine request.</summary>
    public Task<List<AppStateSetting>> UpdateSettingsAsync(
        IReadOnlyList<AppStateSetting> settings, CancellationToken cancellationToken = default) =>
        SendJsonAsync<List<AppStateSetting>, IReadOnlyList<AppStateSetting>>(
            HttpMethod.Put, "settings", settings, cancellationToken);

    public Task<ToolCatalog> GetToolCatalogAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ToolCatalog>("tools/catalog", cancellationToken);

    public Task<List<ToolRegistry>> ListToolRegistryAsync(CancellationToken cancellationToken = default) =>
        ListAsync<ToolRegistry>("tool-registry", cancellationToken);

    public Task<ToolRegistry> CreateToolRegistryAsync(UpsertToolRegistryRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ToolRegistry, UpsertToolRegistryRequest>(HttpMethod.Post, "tool-registry", request, cancellationToken);

    public Task<ToolRegistry> UpdateToolRegistryAsync(string uuid, UpsertToolRegistryRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ToolRegistry, UpsertToolRegistryRequest>(
            HttpMethod.Put, $"tool-registry/{Uri.EscapeDataString(uuid)}", request, cancellationToken);

    public Task<ToolConfigResponse> GetWorkspaceToolsConfigAsync(string uuid, CancellationToken cancellationToken = default) =>
        GetAsync<ToolConfigResponse>($"workspaces/{Uri.EscapeDataString(uuid)}/tools-config", cancellationToken);

    public Task<ToolConfigResponse> UpdateWorkspaceToolsConfigAsync(string uuid, JsonObject config, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ToolConfigResponse, UpdateToolConfigRequest>(
            HttpMethod.Put, $"workspaces/{Uri.EscapeDataString(uuid)}/tools-config",
            new UpdateToolConfigRequest { Config = config }, cancellationToken);

    public Task<ToolConfigResponse> GetThreadToolsConfigAsync(string uuid, CancellationToken cancellationToken = default) =>
        GetAsync<ToolConfigResponse>($"threads/{Uri.EscapeDataString(uuid)}/tools-config", cancellationToken);

    public Task<ToolConfigResponse> UpdateThreadToolsConfigAsync(string uuid, JsonObject config, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ToolConfigResponse, UpdateToolConfigRequest>(
            HttpMethod.Put, $"threads/{Uri.EscapeDataString(uuid)}/tools-config",
            new UpdateToolConfigRequest { Config = config }, cancellationToken);

    public async Task<bool> DeleteToolRegistryAsync(string uuid, CancellationToken cancellationToken = default) =>
        await DeleteAsync($"tool-registry/{Uri.EscapeDataString(uuid)}", cancellationToken);

    public async Task<bool> DeleteThreadToolsConfigAsync(string uuid, CancellationToken cancellationToken = default) =>
        await DeleteAsync($"threads/{Uri.EscapeDataString(uuid)}/tools-config", cancellationToken);

    public Task<List<ModelConfiguration>> ListModelConfigurationsAsync(CancellationToken cancellationToken = default) =>
        ListAsync<ModelConfiguration>("model-configurations", cancellationToken);

    public Task<ModelConfiguration> CreateModelConfigurationAsync(UpsertModelConfigurationRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ModelConfiguration, UpsertModelConfigurationRequest>(
            HttpMethod.Post, "model-configurations", request, cancellationToken);

    public Task<ModelConfiguration> UpdateModelConfigurationAsync(string id, UpsertModelConfigurationRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ModelConfiguration, UpsertModelConfigurationRequest>(
            HttpMethod.Put, $"model-configurations/{Uri.EscapeDataString(id)}", request, cancellationToken);

    public async Task<bool> DeleteModelConfigurationAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = $"model-configurations/{Uri.EscapeDataString(id)}";
        using var response = await Http.DeleteAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        await ReadAsync<object>(response, HttpMethod.Delete, path, cancellationToken);
        return true;
    }

    private Task<T> GetAsync<T>(string path, CancellationToken cancellationToken) =>
        GetAsyncCore<T>(path, cancellationToken);

    private async Task<T> GetAsyncCore<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(path, cancellationToken);
        return await ReadAsync<T>(response, HttpMethod.Get, path, cancellationToken)
            ?? throw new EngineApiException(HttpMethod.Get, path, HttpStatusCode.OK, "empty response body");
    }

    private async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await Http.DeleteAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return true;
        }
        await ReadAsync<object>(response, HttpMethod.Delete, path, cancellationToken);
        return true;
    }

    /// <summary>GET a collection. An empty body is an empty list rather than an error — a missing
    /// list and an empty one mean the same thing to every caller here.</summary>
    private async Task<List<T>> ListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(path, cancellationToken);
        return await ReadAsync<List<T>>(response, HttpMethod.Get, path, cancellationToken) ?? [];
    }

    private async Task<TResult> SendJsonAsync<TResult, TBody>(
        HttpMethod method,
        string path,
        TBody body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await Http.SendAsync(request, cancellationToken);
        return await ReadAsync<TResult>(response, method, path, cancellationToken)
            ?? throw new EngineApiException(method, path, response.StatusCode, "empty response body");
    }

    private static async Task<T?> ReadAsync<T>(
        HttpResponseMessage response,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            string? body = null;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception)
            {
                // The status code is the part worth reporting; a body that won't read is not.
            }
            throw new EngineApiException(method, path, response.StatusCode, body);
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private HttpClient Http => _http ?? throw new InvalidOperationException("Not connected.");

    public async Task DisconnectAsync()
    {
        _closing = true;

        var receiveLoopCts = Interlocked.Exchange(ref _receiveLoopCts, null);
        receiveLoopCts?.Cancel();

        var socket = Interlocked.Exchange(ref _ws, null);
        if (socket is { State: WebSocketState.Open })
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // A transport failure is already handled by dropping this socket.
            }
        }
        socket?.Dispose();
        receiveLoopCts?.Dispose();

        var http = Interlocked.Exchange(ref _http, null);
        http?.Dispose();
        _info = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
