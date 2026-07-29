namespace Subconscious.Engine.Api.Sessions;

/// <summary>
/// Manages active agent sessions and their lifecycles.
/// Thread-safe for concurrent access from multiple WebSocket connections.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Create a new agent session for an incoming WebSocket connection.
    /// </summary>
    /// <param name="webSocket">The WebSocket connection</param>
    /// <returns>The newly created session</returns>
    AgentSession CreateSession(System.Net.WebSockets.WebSocket webSocket);

    /// <summary>
    /// Retrieve an active session by its ID.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <returns>The session if found, null otherwise</returns>
    AgentSession? GetSession(string sessionId);

    /// <summary>
    /// Remove and cleanup a session.
    /// Cancels the session's disconnect token and removes registered tools.
    /// </summary>
    /// <param name="sessionId">Session identifier to remove</param>
    void RemoveSession(string sessionId);

    /// <summary>
    /// Get all currently active sessions.
    /// </summary>
    /// <returns>Enumerable of active sessions</returns>
    IEnumerable<AgentSession> GetActiveSessions();

    /// <summary>
    /// Broadcast an event to all active sessions except the sender.
    /// Used for message.created, thread.created, thread.updated events.
    /// </summary>
    /// <typeparam name="T">Event payload type</typeparam>
    /// <param name="eventType">Event type string (e.g., "message.created")</param>
    /// <param name="payload">Event payload</param>
    /// <param name="excludeSessionId">Optional session ID to exclude (sender)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BroadcastEventAsync<T>(
        string eventType,
        T payload,
        string? excludeSessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an event to a specific session.
    /// </summary>
    /// <typeparam name="T">Event payload type</typeparam>
    /// <param name="sessionId">Target session ID</param>
    /// <param name="eventType">Event type string</param>
    /// <param name="payload">Event payload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendEventAsync<T>(
        string sessionId,
        string eventType,
        T payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleanup orphaned sessions based on timeout or other criteria.
    /// Should be called periodically by a background service.
    /// </summary>
    /// <param name="timeout">Session timeout duration</param>
    /// <returns>Number of sessions cleaned up</returns>
    Task<int> CleanupOrphanedSessionsAsync(TimeSpan timeout);
}
