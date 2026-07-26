using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace Subconscious.Engine.Approval;

/// <summary>
/// Wraps a list of <see cref="AIFunction"/> tools so each one that requires approval under
/// <paramref name="config"/> (per <see cref="OperationClassifier.Classify"/>) is exposed as an
/// <see cref="ApprovalRequiredAIFunction"/>, matching <c>agent.py</c>'s
/// <c>_tool_approval_required</c> predicate passed to pydantic-ai's
/// <c>ApprovalRequiredToolset</c>.
///
/// <para>
/// <b>Experimental API note:</b> <see cref="ApprovalRequiredAIFunction"/> is marked
/// <c>[Experimental("MEAI001")]</c> in the current <c>Microsoft.Extensions.AI</c> release —
/// "for evaluation purposes only and is subject to change or removal in future updates." This
/// type is suppressed at the call site below rather than project-wide so any *other*
/// accidental use of an experimental MEAI001 API elsewhere in the codebase still surfaces as a
/// build error. Re-check this suppression's necessity on every Microsoft.Extensions.AI version
/// bump — if the API graduates to stable, remove the pragma.
/// </para>
///
/// <para>
/// Behaviorally (per <see cref="ApprovalRequiredAIFunction"/>'s own documentation): wrapping a
/// function this way only <em>advertises</em> that approval is required — it does not itself
/// enforce a pause. The caller (the interactive chat loop, in a later Phase 2 increment) is
/// responsible for detecting a pending call against an <see cref="ApprovalRequiredAIFunction"/>
/// and gating actual invocation on a resolved decision, mirroring how
/// <c>DeferredToolRequests</c> is surfaced and resolved in <c>engine.py</c>'s
/// <c>stream_chat_events</c>.
/// </para>
/// </summary>
public static class ApprovalGate
{
    /// <summary>
    /// Return <paramref name="tools"/> with every tool that requires approval under
    /// <paramref name="config"/> wrapped in an <see cref="ApprovalRequiredAIFunction"/>.
    /// Tools that don't require approval are returned unwrapped.
    /// </summary>
    public static IReadOnlyList<AIFunction> Apply(IReadOnlyList<AIFunction> tools, ApprovalConfig config)
    {
        var result = new List<AIFunction>(tools.Count);
        foreach (var tool in tools)
        {
            var kind = OperationClassifier.Classify(tool.Name);
            result.Add(config.RequiresApproval(kind) ? WrapWithApproval(tool) : tool);
        }
        return result;
    }

#pragma warning disable MEAI001 // ApprovalRequiredAIFunction is evaluation-only in this MEAI release; see class remarks.
    private static AIFunction WrapWithApproval(AIFunction tool) => new ApprovalRequiredAIFunction(tool);
#pragma warning restore MEAI001
}
