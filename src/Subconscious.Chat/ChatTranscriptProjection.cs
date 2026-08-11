using System.Collections.ObjectModel;

namespace Subconscious.Chat;

/// <summary>Immutable Markdown output shared by HTML and custom-drawn renderers.</summary>
public sealed record MarkdownProjection(
    string Source,
    string Html,
    string PlainText,
    MarkdownTextDocument NativeText);

/// <summary>An immutable capture of one transcript item and its stable source location.</summary>
public sealed record ChatMessageSnapshot(
    IChatTranscriptMessage Source,
    int SourceIndex,
    string Role,
    DateTime CreatedAt,
    string Timestamp,
    string Content,
    bool IsUser,
    bool IsTool,
    bool IsApproval,
    string ToolInput,
    string ToolOutput,
    string ToolTitle,
    bool IsToolExpanded,
    string ToolExpansionGlyph,
    string ApprovalTitle,
    string ApprovalDescription,
    string ApprovalOperation,
    string ApprovalArguments,
    ChatApprovalStatus ApprovalStatus,
    MarkdownProjection ContentProjection,
    string CanonicalSelectableText);

/// <summary>An immutable renderer-neutral projection of the current transcript.</summary>
public sealed class ChatTranscriptProjection
{
    private ChatTranscriptProjection(IReadOnlyList<ChatMessageSnapshot> messages)
    {
        Messages = messages;
        CanonicalSelectableText = string.Join(
            Environment.NewLine + Environment.NewLine,
            messages.Select(message => message.CanonicalSelectableText)
                .Where(text => !string.IsNullOrEmpty(text)));
    }

    public IReadOnlyList<ChatMessageSnapshot> Messages { get; }

    public string CanonicalSelectableText { get; }

    public static ChatTranscriptProjection Capture(
        IEnumerable<IChatTranscriptMessage> messages,
        MarkdownProjectionService? markdown = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        markdown ??= MarkdownProjectionService.Default;

        var snapshots = messages.Select((message, index) =>
        {
            var approval = message as IChatApprovalRequest;
            var contentProjection = markdown.Project(message.Content);
            var selectableText = approval is not null
                ? BuildApprovalSelectableText(approval)
                : message.IsTool
                    ? BuildToolSelectableText(message)
                    : contentProjection.PlainText;

            return new ChatMessageSnapshot(
                message,
                index,
                message.Role,
                message.CreatedAt,
                message.Timestamp,
                message.Content,
                message.IsUser,
                message.IsTool,
                approval is not null,
                message.ToolInput,
                message.ToolOutput,
                message.ToolTitle,
                message.IsToolExpanded,
                message.ToolExpansionGlyph,
                approval?.ApprovalTitle ?? string.Empty,
                approval?.ApprovalDescription ?? string.Empty,
                approval?.ApprovalOperation ?? string.Empty,
                approval?.ApprovalArguments ?? string.Empty,
                approval?.ApprovalStatus ?? ChatApprovalStatus.Pending,
                contentProjection,
                selectableText);
        }).ToArray();

        return new ChatTranscriptProjection(
            new ReadOnlyCollection<ChatMessageSnapshot>(snapshots));
    }

    private static string BuildApprovalSelectableText(IChatApprovalRequest approval)
    {
        var sections = new[]
        {
            approval.ApprovalTitle,
            approval.ApprovalDescription,
            string.IsNullOrWhiteSpace(approval.ApprovalOperation)
                ? null
                : $"Operation: {approval.ApprovalOperation}",
            string.IsNullOrWhiteSpace(approval.ApprovalArguments)
                ? null
                : $"Arguments:{Environment.NewLine}{approval.ApprovalArguments}"
        };

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            sections.Where(section => !string.IsNullOrWhiteSpace(section))!);
    }

    private static string BuildToolSelectableText(IChatTranscriptMessage message)
    {
        var sections = new[]
        {
            message.ToolTitle,
            string.IsNullOrEmpty(message.ToolInput) ? null : $"Input:{Environment.NewLine}{message.ToolInput}",
            string.IsNullOrEmpty(message.ToolOutput) ? null : $"Output:{Environment.NewLine}{message.ToolOutput}"
        };

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            sections.Where(section => !string.IsNullOrEmpty(section))!);
    }
}
