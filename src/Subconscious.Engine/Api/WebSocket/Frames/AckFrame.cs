namespace Subconscious.Engine.Api.WebSocket.Frames;

/// <summary>
/// Acknowledgment frame sent in response to client actions.
/// </summary>
public class AckFrame
{
    public string Type { get; set; } = "ack";
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
}
