using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

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

    private RuntimeInfo? _info;
    private HttpClient? _http;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _receiveLoopCts;
    private bool _closing;

    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<ChatDeltaEventArgs>? ChatDelta;
    public event EventHandler<ChatDoneEventArgs>? ChatDone;
    public event EventHandler<ChatErrorEventArgs>? ChatError;
    public event EventHandler<ChatMessage>? MessageCreated;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(bool dev)
    {
        _closing = false;
        _info = await EngineDiscovery.DiscoverAsync(dev);

        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{_info.Host}:{_info.Port}/api/v1/"),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _info.Token);

        await OpenSocketAsync();
    }

    private async Task OpenSocketAsync()
    {
        var info = _info ?? throw new InvalidOperationException("Not connected.");
        _ws = new ClientWebSocket();
        var uri = new Uri($"ws://{info.Host}:{info.Port}/api/v1/events?token={Uri.EscapeDataString(info.Token)}");
        await _ws.ConnectAsync(uri, CancellationToken.None);

        await SendFrameAsync("client.hello", new { });

        _receiveLoopCts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoopAsync(_receiveLoopCts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 16];
        try
        {
            while (_ws is { State: WebSocketState.Open } && !cancellationToken.IsCancellationRequested)
            {
                string message;
                using (var stream = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            goto disconnected;
                        }
                        stream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    message = Encoding.UTF8.GetString(stream.ToArray());
                }

                HandleFrame(message);
            }

            disconnected:
            ConnectionStatusChanged?.Invoke(this, false);
            if (!_closing)
            {
                await Task.Delay(2000, CancellationToken.None);
                try
                {
                    await OpenSocketAsync();
                    ConnectionStatusChanged?.Invoke(this, true);
                }
                catch
                {
                    // Best-effort reconnect; surfaced as "disconnected" until it succeeds.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Dispose/DisconnectAsync.
        }
        catch (WebSocketException)
        {
            ConnectionStatusChanged?.Invoke(this, false);
        }
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
        if (_ws is not { State: WebSocketState.Open })
        {
            return Task.CompletedTask;
        }
        var envelope = new { v = 1, type, id, data };
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        return _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>Send a chat message; the reply streams back via <see cref="ChatDelta"/>/<see cref="ChatDone"/>. Returns the turn's correlation id.</summary>
    public string SendChat(string threadUuid, string content, string? modelId = null)
    {
        var turnId = Guid.NewGuid().ToString("N");
        _ = SendFrameAsync("chat.send", new { thread_uuid = threadUuid, content, model_id = modelId }, turnId);
        return turnId;
    }

    public void CancelChat(string? turnId) => _ = SendFrameAsync("chat.cancel", new { turn_id = turnId }, turnId);

    // ── REST ──────────────────────────────────────────────────────────────────
    // Every call goes through SendAsync below, which turns a non-2xx into an
    // EngineApiException. GET /workspaces returning 500 (as it did against a database whose
    // schema predated workspaces.default_model_id) has to read as "the engine returned 500", not
    // as a deserialization failure.

    /// <summary>Every workspace known to the engine, ordered by name (<c>GET /workspaces</c>).</summary>
    public Task<List<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
        ListAsync<Workspace>("workspaces", cancellationToken);

    public Task<Workspace> CreateWorkspaceAsync(string name, string? description = null, string? defaultModelId = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<Workspace, CreateWorkspaceRequest>(
            HttpMethod.Post,
            "workspaces",
            new CreateWorkspaceRequest { Name = name, Description = description, DefaultModelId = defaultModelId },
            cancellationToken);

    public Task<Workspace> UpdateWorkspaceAsync(string uuid, string name, string? description = null, string? defaultModelId = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<Workspace, CreateWorkspaceRequest>(
            HttpMethod.Put,
            $"workspaces/{Uri.EscapeDataString(uuid)}",
            new CreateWorkspaceRequest { Name = name, Description = description, DefaultModelId = defaultModelId },
            cancellationToken);

    public Task<List<ThreadInfo>> ListThreadsAsync(string workspaceUuid, CancellationToken cancellationToken = default) =>
        ListAsync<ThreadInfo>($"workspaces/{Uri.EscapeDataString(workspaceUuid)}/threads", cancellationToken);

    public Task<ThreadInfo> CreateThreadAsync(string workspaceUuid, string? title = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<ThreadInfo, CreateThreadRequest>(
            HttpMethod.Post,
            "threads",
            new CreateThreadRequest { WorkspaceUuid = workspaceUuid, Title = title },
            cancellationToken);

    public Task<List<ChatMessage>> ListMessagesAsync(string threadUuid, CancellationToken cancellationToken = default) =>
        ListAsync<ChatMessage>($"threads/{Uri.EscapeDataString(threadUuid)}/messages", cancellationToken);

    public Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        ListAsync<ModelInfo>("models", cancellationToken);

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
        _receiveLoopCts?.Cancel();
        if (_ws is { State: WebSocketState.Open })
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _ws?.Dispose();
        _http?.Dispose();
        _receiveLoopCts?.Dispose();
    }
}
