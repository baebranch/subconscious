using System.Text.Json;
using System.Text.Json.Nodes;
using Subconscious.Engine.Approval;

namespace Subconscious.Engine.Dispatch;

/// <summary>
/// Context for approval decisions.
/// </summary>
public sealed record ApprovalContext
{
    /// <summary>
    /// Explicit approval configuration (optional).
    /// </summary>
    public ApprovalConfig? ApprovalConfig { get; set; }

    /// <summary>
    /// Workspace ID (for workspace-scoped config).
    /// </summary>
    public long? WorkspaceId { get; set; }

    /// <summary>
    /// Thread ID (for thread-scoped config).
    /// </summary>
    public long? ThreadId { get; set; }

    /// <summary>
    /// Additional approval context data.
    /// </summary>
    public JsonNode? Extra { get; set; }
}

/// <summary>
/// An approval request for a tool call.
/// </summary>
public sealed record ApprovalRequest
{
    /// <summary>
    /// Unique ID for this approval request.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Tool ID being called.
    /// </summary>
    public required string ToolId { get; set; }

    /// <summary>
    /// Provider ID making the call.
    /// </summary>
    public required string ProviderId { get; set; }

    /// <summary>
    /// Profile root of the provider.
    /// </summary>
    public string? ProfileRoot { get; set; }

    /// <summary>
    /// Tool input arguments.
    /// </summary>
    public JsonNode? Input { get; set; }

    /// <summary>
    /// Operation kind (query or mutation).
    /// </summary>
    public OperationKind Kind { get; set; }

    /// <summary>
    /// Request creation time.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Request status.
    /// </summary>
    public ApprovalStatus Status { get; set; }

    /// <summary>
    /// Request resolution time (if resolved).
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Resolver ID (if resolved).
    /// </summary>
    public string? ResolvedBy { get; set; }
}

/// <summary>
/// Status of an approval request.
/// </summary>
public enum ApprovalStatus
{
    /// <summary>
    /// Pending approval.
    /// </summary>
    Pending,

    /// <summary>
    /// Approved.
    /// </summary>
    Approved,

    /// <summary>
    /// Denied.
    /// </summary>
    Denied,

    /// <summary>
    /// Expired (not resolved within timeout).
    /// </summary>
    Expired
}

/// <summary>
/// Gates tool calls that require human approval, ensuring proper routing for HITL.
/// <para>
/// Port of Python's <c>dispatch/approval.py</c>.
/// Extends the base ApprovalGate with provider-specific logic.
/// </summary>
public sealed class PeerApprovalGate
{
    /// <summary>
    /// Check if a tool call from a provider requires approval.
    /// </summary>
    /// <param name="toolId">The tool ID being called.</param>
    /// <param name="providerId">The provider ID making the call.</param>
    /// <param name="profileRoot">The profile root of the provider.</param>
    /// <param name="context">Optional context for approval resolution.</param>
    public bool RequiresApproval(string toolId, string providerId, string? profileRoot = null, ApprovalContext? context = null)
    {
        // Check if tool is classified as a mutation
        var kind = OperationClassifier.Classify(toolId);
        if (kind != OperationKind.Mutation)
        {
            return false;
        }

        // Get approval config for this provider
        var config = ResolveApprovalConfig(providerId, profileRoot, context);

        // Check if approval is required for this tool
        return config.RequiresApproval(kind);
    }

    /// <summary>
    /// Check if a tool call from a provider can be invoked.
    /// </summary>
    /// <param name="toolId">The tool ID being called.</param>
    /// <param name="providerId">The provider ID making the call.</param>
    /// <param name="profileRoot">The profile root of the provider.</param>
    /// <param name="context">Optional context for approval resolution.</param>
    public bool CanInvoke(string toolId, string providerId, string? profileRoot = null, ApprovalContext? context = null)
    {
        // Query tools don't require approval
        var kind = OperationClassifier.Classify(toolId);
        if (kind == OperationKind.Query)
        {
            return true;
        }

        // Check approval requirements
        return !RequiresApproval(toolId, providerId, profileRoot, context);
    }

    /// <summary>
    /// Resolve the approval configuration for a provider.
    /// </summary>
    /// <param name="providerId">The provider ID.</param>
    /// <param name="profileRoot">The profile root.</param>
    /// <param name="context">Optional context.</param>
    private ApprovalConfig ResolveApprovalConfig(string providerId, string? profileRoot, ApprovalContext? context)
    {
        // If context provided with explicit config, use it
        if (context?.ApprovalConfig != null)
        {
            return context.ApprovalConfig;
        }

        // Default config
        return ApprovalConfig.Default;
    }

    /// <summary>
    /// Create an approval request for a tool call.
    /// </summary>
    /// <param name="toolId">The tool ID.</param>
    /// <param name="providerId">The provider ID.</param>
    /// <param name="input">The tool input.</param>
    /// <param name="profileRoot">The profile root.</param>
    /// <param name="correlationId">The correlation ID.</param>
    public ApprovalRequest CreateApprovalRequest(
        string toolId,
        string providerId,
        JsonNode? input,
        string? profileRoot,
        string correlationId)
    {
        var kind = OperationClassifier.Classify(toolId);

        return new ApprovalRequest
        {
            Id = correlationId,
            ToolId = toolId,
            ProviderId = providerId,
            ProfileRoot = profileRoot,
            Input = input,
            Kind = kind,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Mark an approval request as approved.
    /// </summary>
    public void Approve(ApprovalRequest request)
    {
        request.Status = ApprovalStatus.Approved;
        request.ResolvedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark an approval request as denied.
    /// </summary>
    public void Deny(ApprovalRequest request)
    {
        request.Status = ApprovalStatus.Denied;
        request.ResolvedAt = DateTime.UtcNow;
    }
}
