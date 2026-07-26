namespace Subconscious.Engine.Approval;

/// <summary>
/// Human-in-the-loop approval policy: whether <see cref="OperationKind.Query"/> and/or
/// <see cref="OperationKind.Mutation"/> tool calls require explicit user approval before
/// running. Mirrors <c>engine.py</c>'s <c>_DEFAULT_APPROVAL_CONFIG</c> /
/// <c>_normalize_approval_config</c> ({"query": bool, "mutation": bool} persisted as JSON on
/// the workspace/thread rows). Defaults to requiring approval for both, so a
/// newly-configured workspace/thread is safe-by-default.
/// </summary>
/// <param name="RequireApprovalForQueries">True when query-classified tool calls need approval.</param>
/// <param name="RequireApprovalForMutations">True when mutation-classified tool calls need approval.</param>
public sealed record ApprovalConfig(
    bool RequireApprovalForQueries = true,
    bool RequireApprovalForMutations = true)
{
    public static readonly ApprovalConfig Default = new();

    /// <summary>Whether a call classified as <paramref name="kind"/> requires approval under this policy.</summary>
    public bool RequiresApproval(OperationKind kind) => kind switch
    {
        OperationKind.Query => RequireApprovalForQueries,
        OperationKind.Mutation => RequireApprovalForMutations,
        _ => true,
    };
}
