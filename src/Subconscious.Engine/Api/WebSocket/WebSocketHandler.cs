using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Subconscious.Engine.Agents;
using Subconscious.Engine.Api.Events;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Subconscious.Engine.Dispatch;
using Subconscious.Engine.Tools;

namespace Subconscious.Engine.Api.WebSocket;

/// <summary>
/// Creates a <see cref="WebSocketHandler"/> per accepted connection, each with its own DI
/// scope (and therefore its own <see cref="SubconsciousDbContext"/>) so concurrent
/// connections never share a DbContext instance.
/// </summary>
public sealed class WebSocketHandlerFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    public WebSocketHandlerFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public WebSocketHandler Create(global::System.Net.WebSockets.WebSocket socket)
    {
        var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        return new WebSocketHandler(
            socket,
            scope,
            services.GetRequiredService<ILogger<WebSocketHandler>>(),
            services.GetRequiredService<IEventBus>(),
            services.GetRequiredService<ProviderTable>(),
            services.GetRequiredService<ToolDispatcher>(),
            services.GetRequiredService<HandshakeService>(),
            services.GetRequiredService<BaseToolRegistry>(),
            services.GetRequiredService<SubconsciousDbContext>(),
            services.GetRequiredService<AgentManager>());
    }
}

/// <summary>
/// WebSocket handler for the <c>/api/v1/events</c> connection: the frozen compatibility
/// protocol described in translation.md §4.5/§6 (byte-for-byte compatible with the Python
/// engine and the <c>subconscious-code</c> TypeScript client).
/// <para>
/// Envelope: every frame is <c>{ v, type, id?, data? }</c> — <see cref="WsEnvelope"/> — not
/// the flat top-level-fields shape the earlier scaffold used. This matches
/// <c>subconscious-code/src/engine/types.ts</c>'s <c>WSFrame&lt;T&gt;</c> exactly.
/// </para>
/// </summary>
public sealed class WebSocketHandler : IAsyncDisposable
{
    private readonly global::System.Net.WebSockets.WebSocket _webSocket;
    private readonly IServiceScope _scope;
    private readonly ILogger<WebSocketHandler> _logger;
    private readonly IEventBus _eventBus;
    private readonly ProviderTable _providerTable;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly HandshakeService _handshakeService;
    private readonly BaseToolRegistry _toolRegistry;
    private readonly SubconsciousDbContext _db;
    private readonly AgentManager _agentManager;

    public WebSocketHandler(
        global::System.Net.WebSockets.WebSocket webSocket,
        IServiceScope scope,
        ILogger<WebSocketHandler> logger,
        IEventBus eventBus,
        ProviderTable providerTable,
        ToolDispatcher toolDispatcher,
        HandshakeService handshakeService,
        BaseToolRegistry toolRegistry,
        SubconsciousDbContext db,
        AgentManager agentManager)
    {
        _webSocket = webSocket;
        _scope = scope;
        _logger = logger;
        _eventBus = eventBus;
        _providerTable = providerTable;
        _toolDispatcher = toolDispatcher;
        _handshakeService = handshakeService;
        _toolRegistry = toolRegistry;
        _db = db;
        _agentManager = agentManager;
    }

    public async Task RunAsync()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        string? clientId = null;

        _logger.LogInformation("WebSocket session {SessionId} established", sessionId);
        var clientIdBox = new string?[1];
        try
        {
            await ProcessMessagesAsync(sessionId, clientIdBox);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WebSocket session {SessionId}", sessionId);
        }
        finally
        {
            clientId = clientIdBox[0];
            if (clientId is not null)
            {
                _handshakeService.RemoveClient(clientId);
            }
            _logger.LogInformation("WebSocket session {SessionId} closed", sessionId);
        }
    }

    private async Task ProcessMessagesAsync(string sessionId, string?[] clientIdBox)
    {
        var buffer = new byte[1024 * 16];

        while (_webSocket.State == WebSocketState.Open)
        {
            string message;
            using (var stream = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        return;
                    }
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                message = Encoding.UTF8.GetString(stream.ToArray());
            }

            try
            {
                await HandleMessageAsync(sessionId, message, id => clientIdBox[0] = id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                await SendErrorAsync("Internal server error");
            }
        }
    }

    private async Task HandleMessageAsync(string sessionId, string message, Action<string> onClientIdAssigned)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(message);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON frame");
            await SendErrorAsync("Invalid JSON");
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                await SendErrorAsync("Missing 'type' field");
                return;
            }

            var frameType = typeElement.GetString();
            var id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
            var data = root.TryGetProperty("data", out var dataEl) ? dataEl : default;

            _logger.LogDebug("Received frame type: {Type}", frameType);

            switch (frameType)
            {
                case "client.hello":
                    await HandleClientHelloAsync(sessionId, data, onClientIdAssigned);
                    break;
                case "tool.register":
                    await HandleToolRegisterAsync(data);
                    break;
                case "tool.unregister":
                    await HandleToolUnregisterAsync(data);
                    break;
                case "profile.set":
                    await HandleProfileSetAsync(data);
                    break;
                case "chat.send":
                    await HandleChatSendAsync(id, data);
                    break;
                case "chat.cancel":
                    // No in-flight-turn cancellation registry yet; acknowledged as a no-op so
                    // the client's stop button doesn't error, matching graceful degradation.
                    break;
                case "tool.result":
                    await HandleToolResultAsync(data);
                    break;
                case "ping":
                    await SendFrameAsync("pong", (object?)null);
                    break;
                default:
                    await SendErrorAsync($"Unknown frame type: {frameType}");
                    break;
            }
        }
    }

    private async Task HandleClientHelloAsync(string sessionId, JsonElement data, Action<string> onClientIdAssigned)
    {
        string? requestedId = data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("clientId", out var reqIdEl)
            && reqIdEl.ValueKind == JsonValueKind.String
                ? reqIdEl.GetString()
                : null;

        // Re-presenting a still-active id is rejected (another live connection owns it);
        // otherwise the requested id (or a freshly generated one) is granted.
        if (requestedId is not null && _handshakeService.TryGetClient(requestedId, out _))
        {
            await SendFrameAsync("client.hello.reject", new { reason = "Client_ID already in use by an active connection." });
            return;
        }

        var clientId = requestedId ?? _handshakeService.GenerateClientId();
        _handshakeService.RecordHandshake(clientId, sessionId, new ClientHelloMetadata());
        onClientIdAssigned(clientId);

        await SendFrameAsync("client.hello.ack", new { clientId });
    }

    private async Task HandleToolRegisterAsync(JsonElement data)
    {
        var tools = new List<ToolRegistration>();
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("tools", out var toolsProp))
        {
            foreach (var tool in toolsProp.EnumerateArray())
            {
                tools.Add(new ToolRegistration
                {
                    Id = tool.GetProperty("id").GetString()!,
                    Name = tool.TryGetProperty("id", out var nameEl) ? nameEl.GetString()! : string.Empty,
                    Description = tool.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                });
            }
        }

        var providerId = Guid.NewGuid().ToString("N");
        var provider = new Provider(
            new NoOpProviderConnection(),
            providerId,
            new ProviderMetadata { Name = "Client", Type = "client" },
            "default");
        provider.RegisterTools(tools);
        _providerTable.Add(provider);
    }

    private Task HandleToolUnregisterAsync(JsonElement data) => Task.CompletedTask;

    private Task HandleProfileSetAsync(JsonElement data) => Task.CompletedTask;

    private Task HandleToolResultAsync(JsonElement data) => Task.CompletedTask;

    /// <summary>
    /// The interactive chat turn: persist the user message, stream the model's reply as
    /// <c>chat.delta</c> frames, persist the assistant message, then <c>chat.done</c>.
    /// Uses the echo dev model unless <c>model_id</c> names another configured one (no
    /// model-config store exists yet — see translation.md's open Phase 1 secrets-store item
    /// — so only "echo" resolves today).
    /// </summary>
    private async Task HandleChatSendAsync(string? turnId, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("thread_uuid", out var threadUuidEl)
            || !data.TryGetProperty("content", out var contentEl))
        {
            await SendErrorAsync("chat.send requires thread_uuid and content");
            return;
        }

        var threadUuid = threadUuidEl.GetString()!;
        var content = contentEl.GetString() ?? string.Empty;

        var thread = await _db.Threads.FirstOrDefaultAsync(threadUuid);
        if (thread is null)
        {
            await SendChatErrorAsync(turnId, threadUuid, $"Thread '{threadUuid}' not found.");
            return;
        }

        var userMessage = new Message
        {
            Uuid = Guid.NewGuid().ToString(),
            ThreadId = thread.Id,
            Role = "user",
            Content = content,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Messages.Add(userMessage);
        thread.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await PublishMessageCreatedAsync(userMessage, threadUuid);

        var chatClient = _agentManager.BuildChatClient(new ModelConfig("echo", "subconscious", "echo"));
        var assistantUuid = Guid.NewGuid().ToString();
        var assistantText = new System.Text.StringBuilder();

        try
        {
            var history = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(ChatRole.User, content),
            };

            await foreach (var update in chatClient.GetStreamingResponseAsync(history))
            {
                var delta = update.Text;
                if (string.IsNullOrEmpty(delta))
                {
                    continue;
                }
                assistantText.Append(delta);
                await SendFrameAsync("chat.delta", new { thread_uuid = threadUuid, delta }, turnId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "chat.send streaming failed for thread {ThreadUuid}", threadUuid);
            await SendChatErrorAsync(turnId, threadUuid, ex.Message);
            return;
        }

        var assistantMessage = new Message
        {
            Uuid = assistantUuid,
            ThreadId = thread.Id,
            Role = "assistant",
            Content = assistantText.ToString(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.Messages.Add(assistantMessage);
        thread.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await PublishMessageCreatedAsync(assistantMessage, threadUuid);

        await SendFrameAsync("chat.done", new { thread_uuid = threadUuid }, turnId);
    }

    private async Task PublishMessageCreatedAsync(Message message, string threadUuid)
    {
        await _eventBus.PublishAsync(new MessageCreatedEvent
        {
            MessageId = message.Uuid,
            ThreadId = threadUuid,
            Role = message.Role,
            Content = message.Content,
        });
    }

    private Task SendChatErrorAsync(string? turnId, string threadUuid, string error) =>
        SendFrameAsync("chat.error", new { thread_uuid = threadUuid, error }, turnId);

    private Task SendErrorAsync(string message) => SendFrameAsync("error", new { error = message });

    private Task SendFrameAsync<T>(string type, T data, string? id = null)
    {
        var envelope = new WsEnvelope<T>(1, type, id, data);
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        return _webSocket.State == WebSocketState.Open
            ? _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
            : Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await ValueTask.CompletedTask;
    }
}

/// <summary>The wire envelope every frame uses: <c>{ v, type, id?, data? }</c>.</summary>
public sealed record WsEnvelope<T>(int V, string Type, string? Id, T Data);

/// <summary>
/// Placeholder <see cref="ProviderConnection"/> for client-registered tools. Actually
/// routing a <c>tool.call</c> to the owning WebSocket connection (so the client can
/// execute it and reply with <c>tool.result</c>) is not yet wired end-to-end; this keeps
/// <see cref="ToolDispatcher"/> from throwing when a registered tool's presence is queried,
/// without claiming a capability that doesn't exist yet.
/// </summary>
internal sealed class NoOpProviderConnection : ProviderConnection
{
    public Task SendToolCall(string correlationId, string toolId, JsonNode? input, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<JsonNode?> SendToolCallAsync(string correlationId, string toolId, JsonNode? input, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode?>(null);

    public void Dispose()
    {
    }
}

internal static class DbSetExtensions
{
    public static async Task<Data.Entities.Thread?> FirstOrDefaultAsync(this Microsoft.EntityFrameworkCore.DbSet<Data.Entities.Thread> set, string uuid) =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(set, t => t.Uuid == uuid);
}
