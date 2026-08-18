using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Subconscious.Engine.Agents;
using Subconscious.Engine.Api.Events;
using Subconscious.Engine.Configuration;
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
            services.GetRequiredService<AgentManager>(),
            services.GetRequiredService<IModelConfigurationStore>());
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
    private readonly IModelConfigurationStore _modelConfigurations;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _activeTurns = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingApproval> _pendingApprovals = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

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
        AgentManager agentManager,
        IModelConfigurationStore modelConfigurations)
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
        _modelConfigurations = modelConfigurations;
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
                    await StartChatTurnAsync(id, data.Clone());
                    break;
                case "chat.cancel":
                    await HandleChatCancelAsync(id, data);
                    break;
                case "tool.approval.response":
                    await HandleApprovalResponseAsync(id, data);
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

    private async Task StartChatTurnAsync(string? requestedTurnId, JsonElement data)
    {
        var turnId = string.IsNullOrWhiteSpace(requestedTurnId) ? Guid.NewGuid().ToString("N") : requestedTurnId;
        if (_activeTurns.Any())
        {
            await SendErrorAsync("Only one chat turn may run on a connection at a time.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        if (!_activeTurns.TryAdd(turnId, cancellation))
        {
            cancellation.Dispose();
            await SendErrorAsync($"Chat turn '{turnId}' is already running.");
            return;
        }

        _ = RunChatTurnSafelyAsync(turnId, data, cancellation);
    }

    private async Task RunChatTurnSafelyAsync(string turnId, JsonElement data, CancellationTokenSource cancellation)
    {
        try
        {
            await HandleChatSendAsync(turnId, data, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation can arrive before the turn has resolved a thread, so provide a
            // terminal frame here as well; the normal path emits it with the real UUID.
            await SendFrameAsync("chat.cancelled", new { thread_uuid = string.Empty }, turnId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "chat.send failed for turn {TurnId}", turnId);
            await SendChatErrorAsync(turnId, string.Empty, "The chat turn failed unexpectedly.");
        }
        finally
        {
            foreach (var pending in _pendingApprovals.Where(pair => pair.Value.TurnId == turnId).ToArray())
            {
                if (_pendingApprovals.TryRemove(pending.Key, out var removed))
                {
                    removed.Decision.TrySetCanceled(cancellation.Token);
                }
            }

            if (_activeTurns.TryRemove(turnId, out var active))
            {
                active.Dispose();
            }
        }
    }

    private async Task HandleChatCancelAsync(string? envelopeTurnId, JsonElement data)
    {
        var turnId = envelopeTurnId
            ?? (data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("turn_id", out var requested)
                && requested.ValueKind == JsonValueKind.String
                    ? requested.GetString()
                    : null);

        if (turnId is not null && _activeTurns.TryGetValue(turnId, out var cancellation))
        {
            cancellation.Cancel();
        }
    }

    private Task HandleApprovalResponseAsync(string? turnId, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("approval_id", out var approvalIdElement)
            || approvalIdElement.ValueKind != JsonValueKind.String
            || !data.TryGetProperty("decision", out var decisionElement)
            || decisionElement.ValueKind != JsonValueKind.String)
        {
            return SendErrorAsync("tool.approval.response requires approval_id and decision.");
        }

        var approvalId = approvalIdElement.GetString();
        var approved = string.Equals(decisionElement.GetString(), "approve", StringComparison.OrdinalIgnoreCase);
        if (approvalId is null || !_pendingApprovals.TryGetValue(approvalId, out var pending))
        {
            return SendErrorAsync("Approval request was not found or has already completed.");
        }
        if (turnId is not null && !string.Equals(turnId, pending.TurnId, StringComparison.Ordinal))
        {
            return SendErrorAsync("Approval response does not belong to this chat turn.");
        }

        pending.Decision.TrySetResult(approved);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a workspace/thread's effective policy, sends the provider a selected tool set,
    /// and explicitly executes requested functions. Deliberately no automatic-invocation middleware
    /// is used: every call is classified and, where required, paused for a user decision first.
    /// </summary>
    private async Task HandleChatSendAsync(string turnId, JsonElement data, CancellationToken cancellationToken)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("content", out var contentEl))
        {
            await SendErrorAsync("chat.send requires content and either thread_uuid or workspace_uuid");
            return;
        }

        var content = contentEl.GetString() ?? string.Empty;
        var threadUuid = data.TryGetProperty("thread_uuid", out var threadUuidEl) ? threadUuidEl.GetString() : null;
        var workspaceUuid = data.TryGetProperty("workspace_uuid", out var workspaceUuidEl) ? workspaceUuidEl.GetString() : null;
        var requestedModelId = data.TryGetProperty("model_id", out var modelIdEl) ? modelIdEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(threadUuid) == string.IsNullOrWhiteSpace(workspaceUuid))
        {
            await SendErrorAsync("chat.send requires exactly one of thread_uuid or workspace_uuid");
            return;
        }

        Data.Entities.Thread? thread;
        Data.Entities.Workspace? workspace;
        if (!string.IsNullOrWhiteSpace(threadUuid))
        {
            thread = await _db.Threads.FirstOrDefaultAsync(threadUuid);
            if (thread is null)
            {
                await SendChatErrorAsync(turnId, threadUuid, $"Thread '{threadUuid}' not found.");
                return;
            }
            workspace = await _db.Workspaces.FirstOrDefaultAsync(candidate => candidate.Id == thread.WorkspaceId, cancellationToken);
        }
        else
        {
            workspace = await _db.Workspaces.FirstOrDefaultAsync(candidate => candidate.Uuid == workspaceUuid, cancellationToken);
            if (workspace is null)
            {
                await SendChatErrorAsync(turnId, string.Empty, $"Workspace '{workspaceUuid}' not found.");
                return;
            }
            thread = null;
        }

        if (workspace is null)
        {
            await SendChatErrorAsync(turnId, threadUuid ?? string.Empty, "The thread's workspace no longer exists.");
            return;
        }

        var effectiveModelId = requestedModelId ?? thread?.DefaultModelId ?? workspace.DefaultModelId ?? "echo";
        var modelConfig = string.Equals(effectiveModelId, "echo", StringComparison.OrdinalIgnoreCase)
            ? new ModelConfig("echo", "subconscious", "echo")
            : await _modelConfigurations.ResolveAsync(effectiveModelId, cancellationToken);
        if (modelConfig is null)
        {
            await SendChatErrorAsync(turnId, threadUuid ?? string.Empty, $"Model configuration '{effectiveModelId}' was not found.");
            return;
        }

        if (thread is null)
        {
            var now = DateTime.UtcNow;
            thread = new Data.Entities.Thread
            {
                Uuid = Guid.NewGuid().ToString(),
                WorkspaceId = workspace.Id,
                Title = CreateThreadTitle(content),
                DefaultModelId = modelConfig.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Threads.Add(thread);
            await _db.SaveChangesAsync(cancellationToken);
            threadUuid = thread.Uuid;
            await _eventBus.PublishAsync(new ThreadCreatedEvent { ThreadId = threadUuid, WorkspaceId = workspace.Uuid, Title = thread.Title });
        }
        else
        {
            // Desktop can pre-create a draft when it needs to persist an explicit tool policy
            // before the first prompt. Preserve the usual generated-title behavior for that
            // otherwise empty thread.
            if (string.IsNullOrWhiteSpace(thread.Title))
            {
                thread.Title = CreateThreadTitle(content);
            }
            if (!string.Equals(thread.DefaultModelId, modelConfig.Id, StringComparison.Ordinal))
            {
                thread.DefaultModelId = modelConfig.Id;
            }
        }

        var activeThreadUuid = threadUuid ?? throw new InvalidOperationException("A chat turn must have a persisted thread.");
        var userMessage = new Message { Uuid = Guid.NewGuid().ToString(), ThreadId = thread.Id, Role = "user", Content = content, CreatedAt = DateTime.UtcNow };
        _db.Messages.Add(userMessage);
        thread.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await PublishMessageCreatedAsync(userMessage, activeThreadUuid);

        try
        {
            var toolConfig = ToolsConfig.FromJson(Subconscious.Engine.Api.Services.ToolConfigJson.ResolveNode(workspace.ToolsConfig, thread.ToolsConfig));
            var approvalConfig = Subconscious.Engine.Approval.ApprovalConfig.FromJson(thread.ApprovalConfig ?? workspace.ApprovalConfig);
            var context = new EngineContext
            {
                Database = _db,
                WorkspaceId = workspace.Id,
                ThreadId = thread.Id,
                ApprovalConfig = approvalConfig,
            };
            var executableTools = _toolRegistry.GetToolsForConfig(toolConfig, context);
            var modelTools = Subconscious.Engine.Approval.ApprovalGate.Apply(executableTools, approvalConfig);
            var history = await LoadChatHistoryAsync(thread.Id, cancellationToken);
            using var chatClient = _agentManager.BuildChatClient(modelConfig);
            var assistantText = await ExecuteToolAwareChatAsync(
                chatClient,
                history,
                executableTools,
                modelTools,
                approvalConfig,
                turnId,
                activeThreadUuid,
                thread,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var assistantMessage = new Message { Uuid = Guid.NewGuid().ToString(), ThreadId = thread.Id, Role = "assistant", Content = assistantText, CreatedAt = DateTime.UtcNow };
            _db.Messages.Add(assistantMessage);
            thread.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await PublishMessageCreatedAsync(assistantMessage, activeThreadUuid);
            await SendFrameAsync("chat.done", new { thread_uuid = activeThreadUuid }, turnId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SendFrameAsync("chat.cancelled", new { thread_uuid = activeThreadUuid }, turnId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "chat.send tool-aware execution failed for thread {ThreadUuid}", activeThreadUuid);
            await SendChatErrorAsync(turnId, activeThreadUuid, exception.Message);
        }
    }

    private async Task<List<Microsoft.Extensions.AI.ChatMessage>> LoadChatHistoryAsync(int threadId, CancellationToken cancellationToken)
    {
        var messages = await _db.Messages.Where(message => message.ThreadId == threadId)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(cancellationToken);
        return messages.Where(message => message.Role is "user" or "assistant" or "system")
            .Select(message => new Microsoft.Extensions.AI.ChatMessage(message.Role switch
            {
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                _ => ChatRole.User,
            }, message.Content))
            .ToList();
    }

    private async Task<string> ExecuteToolAwareChatAsync(
        IChatClient chatClient,
        List<Microsoft.Extensions.AI.ChatMessage> history,
        IReadOnlyList<AIFunction> executableTools,
        IReadOnlyList<AIFunction> modelTools,
        Subconscious.Engine.Approval.ApprovalConfig approvalConfig,
        string turnId,
        string threadUuid,
        Data.Entities.Thread thread,
        CancellationToken cancellationToken)
    {
        var toolsByName = executableTools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var options = new ChatOptions { Tools = [.. modelTools] };
        var visibleText = new StringBuilder();

        for (var iteration = 0; iteration < 8; iteration++)
        {
            async IAsyncEnumerable<ChatResponseUpdate> StreamUpdates()
            {
                await foreach (var update in chatClient
                    .GetStreamingResponseAsync(history, options, cancellationToken)
                    .WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        visibleText.Append(update.Text);
                        await SendFrameAsync(
                            "chat.delta",
                            new { thread_uuid = threadUuid, delta = update.Text },
                            turnId);
                    }
                    yield return update;
                }
            }

            var response = await StreamUpdates().ToChatResponseAsync(cancellationToken);
            var calls = response.Messages.SelectMany(message => message.Contents.OfType<FunctionCallContent>()).ToList();
            history.AddRange(response.Messages);
            if (calls.Count == 0)
            {
                return visibleText.ToString();
            }

            // Bedrock requires every tool-use id from this assistant message to be answered by
            // tool-result blocks in one immediate following user message. Execute calls
            // sequentially so approval remains per-call, then append their results together.
            var toolResults = new List<AIContent>();
            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object result;
                if (!toolsByName.TryGetValue(call.Name, out var function))
                {
                    result = $"The requested tool '{call.Name}' is not enabled.";
                }
                else if (approvalConfig.RequiresApproval(Subconscious.Engine.Approval.OperationClassifier.Classify(function.Name))
                    && !await RequestApprovalAsync(turnId, threadUuid, function.Name, call.Arguments ?? new Dictionary<string, object?>(), cancellationToken))
                {
                    result = "The user denied this tool call.";
                }
                else
                {
                    try
                    {
                        result = await function.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken) ?? "(no result)";
                    }
                    catch (Exception exception)
                    {
                        result = $"Tool execution failed: {exception.Message}";
                    }
                }

                var input = JsonSerializer.Serialize(call.Arguments);
                var output = JsonSerializer.Serialize(result);
                var toolMessage = new Message
                {
                    Uuid = Guid.NewGuid().ToString(),
                    ThreadId = thread.Id,
                    Role = "tool",
                    Content = JsonSerializer.Serialize(new { toolName = call.Name, input = JsonDocument.Parse(input).RootElement, output = JsonDocument.Parse(output).RootElement }),
                    CreatedAt = DateTime.UtcNow,
                };
                _db.Messages.Add(toolMessage);
                await _db.SaveChangesAsync(cancellationToken);
                await PublishMessageCreatedAsync(toolMessage, threadUuid);
                toolResults.Add(new FunctionResultContent(call.CallId, result));
            }

            history.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Tool, toolResults));
        }

        throw new InvalidOperationException("The model exceeded the maximum of eight tool-call rounds.");
    }

    private async Task<bool> RequestApprovalAsync(
        string turnId,
        string threadUuid,
        string toolName,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var approvalId = Guid.NewGuid().ToString("N");
        var pending = new PendingApproval(turnId, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        if (!_pendingApprovals.TryAdd(approvalId, pending))
        {
            throw new InvalidOperationException("Could not create a unique approval request.");
        }

        try
        {
            await SendFrameAsync("tool.approval.request", new
            {
                approval_id = approvalId,
                thread_uuid = threadUuid,
                tool_name = toolName,
                arguments,
                operation = Subconscious.Engine.Approval.OperationClassifier.Classify(toolName).ToString().ToLowerInvariant(),
            }, turnId);
            return await pending.Decision.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pendingApprovals.TryRemove(approvalId, out _);
        }
    }

    private sealed record PendingApproval(string TurnId, TaskCompletionSource<bool> Decision);

    /// <summary>Generates a concise, stable title for a just-materialized draft without requiring
    /// another model round trip. Whitespace is normalized so pasted multiline prompts still have
    /// a usable one-line history label.</summary>
    private static string CreateThreadTitle(string content)
    {
        var normalized = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length switch
        {
            0 => "New conversation",
            <= 60 => normalized,
            _ => normalized[..57] + "…",
        };
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

    private async Task SendFrameAsync<T>(string type, T data, string? id = null)
    {
        var envelope = new WsEnvelope<T>(1, type, id, data);
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        if (_webSocket.State != WebSocketState.Open)
        {
            return;
        }

        await _sendLock.WaitAsync();
        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var cancellation in _activeTurns.Values)
        {
            cancellation.Cancel();
        }
        _sendLock.Dispose();
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
