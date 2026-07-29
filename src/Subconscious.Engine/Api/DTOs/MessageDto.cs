namespace Subconscious.Engine.Api.DTOs;

/// <summary>
/// Data transfer object for chat messages.
/// </summary>
public record MessageDto
{
    public required string Uuid { get; init; }
    public required string ThreadUuid { get; init; }
    public required string Role { get; init; } // "user", "assistant", "system", "tool"
    public required string Content { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Request to send a chat message.
/// </summary>
public record SendMessageRequest
{
    public required string ThreadUuid { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
}

/// <summary>
/// List of messages response.
/// </summary>
public record MessagesResponse
{
    public required List<MessageDto> Messages { get; init; }
}
