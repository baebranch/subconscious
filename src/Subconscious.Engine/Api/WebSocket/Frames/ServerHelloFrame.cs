using Subconscious.Engine.Api.Sessions;

namespace Subconscious.Engine.Api.WebSocket.Frames;

/// <summary>
/// Server hello frame sent after WebSocket connection is established.
/// </summary>
public class ServerHelloFrame
{
    public string Type { get; set; } = "server.hello";
    public string SessionId { get; set; } = string.Empty;
    public string ServerVersion { get; set; } = string.Empty;
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public ExecutionProfile? ExecutionProfile { get; set; }
}
