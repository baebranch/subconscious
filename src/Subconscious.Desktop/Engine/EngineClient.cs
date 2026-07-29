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
    public async Task<List<Workspace>> ListWorkspacesAsync() =>
        await Http.GetFromJsonAsync<List<Workspace>>("workspaces") ?? [];

    public async Task<Workspace> CreateWorkspaceAsync(string name, string? description = null) =>
        await (await Http.PostAsJsonAsync("workspaces", new CreateWorkspaceRequest { Name = name, Description = description }))
            .Content.ReadFromJsonAsync<Workspace>() ?? throw new InvalidOperationException("Empty response creating workspace.");

    public async Task<List<ThreadInfo>> ListThreadsAsync(string workspaceUuid) =>
        await Http.GetFromJsonAsync<List<ThreadInfo>>($"workspaces/{workspaceUuid}/threads") ?? [];

    public async Task<ThreadInfo> CreateThreadAsync(string workspaceUuid, string? title = null) =>
        await (await Http.PostAsJsonAsync("threads", new CreateThreadRequest { WorkspaceUuid = workspaceUuid, Title = title }))
            .Content.ReadFromJsonAsync<ThreadInfo>() ?? throw new InvalidOperationException("Empty response creating thread.");

    public async Task<List<ChatMessage>> ListMessagesAsync(string threadUuid) =>
        await Http.GetFromJsonAsync<List<ChatMessage>>($"threads/{threadUuid}/messages") ?? [];

    public async Task<List<ModelInfo>> ListModelsAsync() =>
        await Http.GetFromJsonAsync<List<ModelInfo>>("models") ?? [];

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
