namespace Subconscious.Engine.Dispatch;

/// <summary>
/// Classifies incoming connections by type (client, engine, health check, etc.).
/// <para>
/// Port of Python's <c>dispatch/classifier.py</c>.
/// Used to determine how to handle different connection types.
/// </para>
/// </summary>
public sealed class ConnectionClassifier
{
    /// <summary>
    /// Classify a connection based on its request or headers.
    /// </summary>
    /// <param name="requestPath">The request path.</param>
    /// <param name="headers">Connection headers.</param>
    /// <param name="protocol">The connection protocol.</param>
    /// <returns>The connection type.</returns>
    public ConnectionType Classify(
        string? requestPath = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? protocol = null)
    {
        // Check protocol first
        if (protocol == "websocket")
        {
            return ConnectionType.WebSocket;
        }

        // Check request path
        if (!string.IsNullOrEmpty(requestPath))
        {
            switch (requestPath.ToLowerInvariant())
            {
                case "/api/v1/health":
                case "/health":
                    return ConnectionType.HealthCheck;

                case "/api/v1/events":
                    return ConnectionType.ClientWebSocket;

                case "/api/v1/stream":
                    return ConnectionType.EventStream;

                case "/api/v1/agui":
                    return ConnectionType.AgUi;

                case "/api/v1/runtime.json":
                    return ConnectionType.RuntimeDiscovery;
            }
        }

        // Check headers for client hints
        if (headers != null)
        {
            if (headers.TryGetValue("X-Subconscious-Client", out var clientHeader))
            {
                return ConnectionType.ClientWebSocket;
            }

            if (headers.TryGetValue("X-Subconscious-Type", out var typeHeader))
            {
                return typeHeader.ToLowerInvariant() switch
                {
                    "engine" => ConnectionType.Engine,
                    "client" => ConnectionType.ClientWebSocket,
                    "health" => ConnectionType.HealthCheck,
                    _ => ConnectionType.Unknown
                };
            }
        }

        return ConnectionType.Unknown;
    }

    /// <summary>
    /// Check if a connection type is a client connection.
    /// </summary>
    public bool IsClientConnection(ConnectionType type)
    {
        return type is ConnectionType.ClientWebSocket
            or ConnectionType.AgUi
            or ConnectionType.EventStream;
    }

    /// <summary>
    /// Check if a connection type is an engine connection.
    /// </summary>
    public bool IsEngineConnection(ConnectionType type)
    {
        return type == ConnectionType.Engine;
    }
}

/// <summary>
/// Types of connections.
/// </summary>
public enum ConnectionType
{
    /// <summary>
    /// Unknown connection type.
    /// </summary>
    Unknown,

    /// <summary>
    /// HTTP health check connection.
    /// </summary>
    HealthCheck,

    /// <summary>
    /// WebSocket connection from a client.
    /// </summary>
    ClientWebSocket,

    /// <summary>
    /// WebSocket connection from another engine (for a2a-protocol).
    /// </summary>
    Engine,

    /// <summary>
    /// AG-UI connection (SSE-based).
    /// </summary>
    AgUi,

    /// <summary>
    /// Server-Sent Events stream for engine events.
    /// </summary>
    EventStream,

    /// <summary>
    /// Runtime discovery endpoint.
    /// </summary>
    RuntimeDiscovery,

    /// <summary>
    /// Generic WebSocket (no classification).
    /// </summary>
    WebSocket
}
