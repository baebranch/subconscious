using System.Windows.Input;
using Subconscious.Chat;

namespace Subconscious.Chat.Debug;

public sealed class SampleApprovalRequest : SampleMessage, IChatApprovalRequest
{
    private readonly DelegateCommand _approveCommand;
    private readonly DelegateCommand _denyCommand;
    private ChatApprovalStatus _approvalStatus;

    public SampleApprovalRequest(string title, string description, string operation,
        string arguments, DateTime createdAt) : base("approval", description, createdAt)
    {
        ApprovalTitle = title;
        ApprovalDescription = description;
        ApprovalOperation = operation;
        ApprovalArguments = arguments;
        _approveCommand = new DelegateCommand(
            () => Decide(ChatApprovalStatus.Approved), () => ApprovalStatus == ChatApprovalStatus.Pending);
        _denyCommand = new DelegateCommand(
            () => Decide(ChatApprovalStatus.Denied), () => ApprovalStatus == ChatApprovalStatus.Pending);
    }

    public string ApprovalTitle { get; }
    public string ApprovalDescription { get; }
    public string ApprovalOperation { get; }
    public string ApprovalArguments { get; }
    public ChatApprovalStatus ApprovalStatus => _approvalStatus;
    public ICommand ApproveCommand => _approveCommand;
    public ICommand DenyCommand => _denyCommand;

    private void Decide(ChatApprovalStatus status)
    {
        if (_approvalStatus != ChatApprovalStatus.Pending)
        {
            return;
        }

        _approvalStatus = status;
        Console.WriteLine($"[Human approval] {status}: {ApprovalOperation}");
        OnPropertyChanged(nameof(ApprovalStatus));
        _approveCommand.RaiseCanExecuteChanged();
        _denyCommand.RaiseCanExecuteChanged();
    }
}
