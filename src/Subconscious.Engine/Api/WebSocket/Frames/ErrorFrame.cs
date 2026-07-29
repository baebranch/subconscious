namespace Subconscious.Engine.Api.WebSocket.Frames;

/// <summary>
/// Error frame sent when something goes wrong.
/// </summary>
public class ErrorFrame
{
    public string Type { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Details { get; set; }
}
