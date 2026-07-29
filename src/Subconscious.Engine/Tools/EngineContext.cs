using System.Text.Json.Nodes;
using Subconscious.Engine.Approval;
using Subconscious.Engine.Data;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Routes a tool call out to a connected client through the engine's Provider table, returning
/// the client's tool result. The .NET analog of <c>EngineContext.tool_dispatch</c> in
/// <c>tools/__init__.py</c>: an <c>async (tool_id, input) -&gt; ToolResult-dict</c> callable
/// injected per turn by the WebSocket <c>chat.send</c> handler.
/// <see langword="null"/> on in-process (desktop/CLI) turns with no connected clients.
/// </summary>
/// <param name="toolId">Fully-qualified tool id as registered by the client.</param>
/// <param name="input">Tool arguments as a JSON object.</param>
/// <param name="cancellationToken">Cancels an in-flight dispatch.</param>
public delegate Task<JsonNode?> ToolDispatchDelegate(
    string toolId,
    JsonNode? input,
    CancellationToken cancellationToken = default);

/// <summary>
/// Dependency object handed to every built-in tool when its <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// is created. Direct port of <c>tools/__init__.py</c>'s <c>EngineContext</c> dataclass, which
/// pydantic-ai injected via <c>RunContext[EngineContext]</c>.
///
/// <para>
/// <b>Injection differs by necessity.</b> pydantic-ai passes a <c>RunContext</c> as the first
/// parameter of every tool function, and it is stripped from the JSON schema the model sees.
/// <c>Microsoft.Extensions.AI</c> builds an <c>AIFunction</c> from a delegate and derives the
/// schema from <em>all</em> its parameters, so a context parameter would leak into the schema.
/// Instead, tool modules take the context at construction time
/// (<see cref="IToolModule.CreateTools"/>) and close over it — the tool set is therefore built
/// per turn, exactly the granularity at which <c>EngineContext</c> was constructed in Python.
/// </para>
/// </summary>
public sealed class EngineContext
{
    /// <summary>
    /// The database handle for DB-backed tools (todo/memory/notes/contacts/knowledge).
    /// Phase 1 EF Core DbContext now implemented and ready for tool modules.
    /// </summary>
    public SubconsciousDbContext? Database { get; init; }

    /// <summary>Workspace the current turn belongs to.</summary>
    public required long WorkspaceId { get; init; }

    /// <summary>Thread the current turn belongs to.</summary>
    public required long ThreadId { get; init; }

    /// <summary>
    /// Root data directory, used to scope filesystem tools. Empty string when unset, matching
    /// the Python default.
    /// </summary>
    public string DataDir { get; init; } = string.Empty;

    /// <summary>Resolved human-in-the-loop approval policy for this run.</summary>
    public ApprovalConfig ApprovalConfig { get; init; } = ApprovalConfig.Default;

    /// <summary>
    /// Per-turn Provider-table-backed tool dispatcher, or <see langword="null"/> for
    /// in-process turns with no connected clients.
    /// </summary>
    public ToolDispatchDelegate? ToolDispatch { get; init; }

    /// <summary>
    /// A context suitable only for enumerating the tool catalog (names/descriptions/schemas).
    /// It carries no database handle and no real workspace or thread, so tools built from it
    /// must never be invoked. Used by <see cref="BaseToolRegistry.Catalog"/> so the catalog is
    /// derived from the real tool definitions rather than a hand-maintained duplicate list.
    /// </summary>
    public static EngineContext ForCatalog { get; } = new() { WorkspaceId = 0, ThreadId = 0 };
}
