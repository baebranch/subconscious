using Subconscious.Desktop.Engine;

namespace Subconscious.Terminal;

internal abstract record UiEvent;
internal sealed record KeyPressed(ConsoleKeyInfo Key) : UiEvent;
internal sealed record LineSubmitted(string Text) : UiEvent;
internal sealed record InputClosed : UiEvent;
internal sealed record ConnectionChanged(bool Connected) : UiEvent;
internal sealed record DeltaReceived(ChatDeltaEventArgs Value) : UiEvent;
internal sealed record TurnCompleted(ChatDoneEventArgs Value) : UiEvent;
internal sealed record TurnFailed(ChatErrorEventArgs Value) : UiEvent;
internal sealed record TurnCancelled(ChatCancelledEventArgs Value) : UiEvent;
internal sealed record ApprovalRequested(ToolApprovalRequestEventArgs Value) : UiEvent;
internal sealed record TerminalResized(int Width, int Height) : UiEvent;

internal enum OverlayKind
{
    Workspaces,
    Threads,
    Models,
}

internal sealed record SelectionItem(string Id, string Label);

internal sealed class SelectionOverlay
{
    public SelectionOverlay(OverlayKind kind, string title, IReadOnlyList<SelectionItem> items)
    {
        Kind = kind;
        Title = title;
        Items = items;
    }

    public OverlayKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<SelectionItem> Items { get; }
    public int SelectedIndex { get; set; }
    public SelectionItem? Selected => Items.Count == 0 ? null : Items[SelectedIndex];
}

internal sealed record ModelChoice(string Id, string Label);
internal sealed record PendingApproval(ToolApprovalRequestEventArgs Request, bool ApproveSelected = false);

internal sealed record TerminalView(
    string Status,
    string StreamingText,
    string ComposerText,
    int ComposerCaret,
    bool Busy,
    SelectionOverlay? Selection,
    PendingApproval? Approval);
