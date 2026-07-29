using System.Text.Json;

namespace Subconscious.Engine.Api.DTOs;

/// <summary>
/// Base class for all WebSocket frames exchanged between client and server.
/// </summary>
public abstract record WebSocketFrame
{
    /// <summary>
    /// Frame type identifier (e.g., "client.hello", "chat.send", "tool.call").
    /// </summary>
    public required string Type { get; init; }
}

// ============================================================================
// Client → Server Frames
// ============================================================================

/// <summary>
/// Client initiates connection and sends capabilities.
/// </summary>
public record ClientHelloFrame : WebSocketFrame
{
    public required string ClientId { get; init; }
    public required string ClientVersion { get; init; }
    public string? ClientName { get; init; }
    public Dictionary<string, object>? Capabilities { get; init; }
}

/// <summary>
/// Set or update the user profile for this session.
/// </summary>
public record ProfileSetFrame : WebSocketFrame
{
    public required string WorkspaceUuid { get; init; }
    public string? ThreadUuid { get; init; }
    public string? DefaultModelId { get; init; }
}

/// <summary>
/// Register a custom tool available in the client.
/// </summary>
public record ToolRegisterFrame : WebSocketFrame
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonDocument ParametersSchema { get; init; }
}

/// <summary>
/// Client sends a chat message.
/// </summary>
public record ChatSendFrame : WebSocketFrame
{
    public required string ThreadUuid { get; init; }
    public required string Content { get; init; }
    public string? Role { get; init; } // defaults to "user"
    public string? ModelId { get; init; }
}

/// <summary>
/// Client returns the result of a tool call.
/// </summary>
public record ToolResultFrame : WebSocketFrame
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public JsonDocument? Result { get; init; }
    public string? Error { get; init; }
}

// ============================================================================
// Server → Client Frames
// ============================================================================

/// <summary>
/// Server acknowledges the client connection.
/// </summary>
public record ServerHelloFrame : WebSocketFrame
{
    public required string SessionId { get; init; }
    public required string ServerVersion { get; init; }
    public required List<string> AvailableTools { get; init; }
    public required List<string> AvailableModels { get; init; }
}

/// <summary>
/// Streaming chat response delta (partial content).
/// </summary>
public record ChatDeltaFrame : WebSocketFrame
{
    public required string MessageUuid { get; init; }
    public required string ThreadUuid { get; init; }
    public required string Delta { get; init; }
    public string? Role { get; init; }
}

/// <summary>
/// Server requests the client to execute a tool.
/// </summary>
public record ToolCallFrame : WebSocketFrame
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public required JsonDocument Arguments { get; init; }
}

/// <summary>
/// Notification that a message was created.
/// </summary>
public record MessageCreatedFrame : WebSocketFrame
{
    public required MessageDto Message { get; init; }
}

/// <summary>
/// Notification that a thread was created.
/// </summary>
public record ThreadCreatedFrame : WebSocketFrame
{
    public required ThreadDto Thread { get; init; }
}

/// <summary>
/// Notification that a thread was updated.
/// </summary>
public record ThreadUpdatedFrame : WebSocketFrame
{
    public required ThreadDto Thread { get; init; }
}

/// <summary>
/// Error response frame.
/// </summary>
public record ErrorFrame : WebSocketFrame
{
    public required string Error { get; init; }
    public string? Details { get; init; }
}
