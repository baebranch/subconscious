using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Subconscious.Engine.Api.Sessions;

/// <summary>
/// Thread-safe manager for agent sessions.
/// Handles session lifecycle, event broadcasting, and cleanup.
/// </summary>
public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    public AgentSession CreateSession(System.Net.WebSockets.WebSocket webSocket)
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new AgentSession
        {
            SessionId = sessionId,
            WebSocket = webSocket,
            ConnectedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        if (!_sessions.TryAdd(sessionId, session))
        {
            throw new InvalidOperationException($"Failed to register session {sessionId}");
        }

        _logger.LogInformation("Session {SessionId} created", sessionId);
        return session;
    }

    public AgentSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public void RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            // Cancel any pending operations for this session
            session.DisconnectToken.Cancel();
            session.DisconnectToken.Dispose();

            // Close WebSocket if still open
            if (session.WebSocket?.State == WebSocketState.Open ||
                session.WebSocket?.State == WebSocketState.CloseReceived)
            {
                try
                {
                    _ = session.WebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Session terminated",
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing WebSocket for session {SessionId}", sessionId);
                }
            }

            _logger.LogInformation(
                "Session {SessionId} removed (ClientId: {ClientId}, Tools: {ToolCount}, Duration: {Duration})",
                sessionId,
                session.ClientId ?? "unknown",
                session.RegisteredTools.Count,
                DateTime.UtcNow - session.ConnectedAt);
        }
    }

    public IEnumerable<AgentSession> GetActiveSessions()
    {
        return _sessions.Values;
    }

    public async Task BroadcastEventAsync<T>(
        string eventType,
        T payload,
        string? excludeSessionId = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = new
        {
            type = eventType,
            payload,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = _sessions.Values
            .Where(s => s.SessionId != excludeSessionId &&
                       s.WebSocket?.State == WebSocketState.Open)
            .Select(s => SendBytesAsync(s, bytes, cancellationToken));

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogDebug(
                "Broadcast {EventType} to {Count} sessions",
                eventType,
                tasks.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting {EventType}", eventType);
        }
    }

    public async Task SendEventAsync<T>(
        string sessionId,
        string eventType,
        T payload,
        CancellationToken cancellationToken = default)
    {
        var session = GetSession(sessionId);
        if (session?.WebSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot send {EventType} to closed session {SessionId}", eventType, sessionId);
            return;
        }

        var envelope = new
        {
            type = eventType,
            payload,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);

        await SendBytesAsync(session, bytes, cancellationToken);
        _logger.LogDebug("Sent {EventType} to session {SessionId}", eventType, sessionId);
    }

    public async Task<int> CleanupOrphanedSessionsAsync(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        var orphaned = _sessions.Values
            .Where(s => s.LastActivityAt < cutoff ||
                       s.WebSocket?.State == WebSocketState.Aborted ||
                       s.WebSocket?.State == WebSocketState.Closed)
            .ToList();

        foreach (var session in orphaned)
        {
            _logger.LogInformation(
                "Cleaning up orphaned session {SessionId} (LastActivity: {LastActivity}, State: {State})",
                session.SessionId,
                session.LastActivityAt,
                session.WebSocket?.State);

            RemoveSession(session.SessionId);
        }

        return orphaned.Count;
    }

    private async Task SendBytesAsync(
        AgentSession session,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (session.WebSocket?.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await session.WebSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);

            // Update last activity timestamp
            session.LastActivityAt = DateTime.UtcNow;
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(
                ex,
                "WebSocket error sending to session {SessionId}",
                session.SessionId);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected, don't log
        }
    }
}
