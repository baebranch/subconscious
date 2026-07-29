namespace Subconscious.Engine.Api.WebSocket.Frames;

/// <summary>
/// Execution profile containing workspace, thread, and model context.
/// </summary>
public class ExecutionProfile
{
    public required string WorkspaceUuid { get; init; }
    public string? ThreadUuid { get; init; }
    public string? ModelId { get; init; }
}
