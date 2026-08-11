using System.Windows.Input;

namespace Subconscious.Chat;

/// <summary>Current decision state for a human-in-the-loop transcript request.</summary>
public enum ChatApprovalStatus
{
    Pending,
    Approved,
    Denied,
}

/// <summary>
/// A transcript item that pauses execution until a person allows or denies an operation.
/// It remains UI-neutral so native hosts can bind their own engine request and commands.
/// </summary>
public interface IChatApprovalRequest : IChatTranscriptMessage
{
    string ApprovalTitle { get; }
    string ApprovalDescription { get; }
    string ApprovalOperation { get; }
    string ApprovalArguments { get; }
    ChatApprovalStatus ApprovalStatus { get; }
    ICommand ApproveCommand { get; }
    ICommand DenyCommand { get; }
}
