using System.Windows.Input;
using Subconscious.Chat;

namespace Subconscious.Desktop.ViewModels;

/// <summary>A policy-protected tool invocation rendered as a native transcript item.</summary>
public sealed class ToolApprovalMessageViewModel : ViewModelBase, IChatApprovalRequest
{
    private readonly Action<bool> _resolve;
    private readonly Command _approveCommand;
    private readonly Command _denyCommand;
    private bool _isResolved;

    public ToolApprovalMessageViewModel(string toolName, string operation, string arguments, Action<bool> resolve)
    {
        CreatedAt = DateTime.UtcNow;
        ApprovalDescription = $"Allow {toolName} to run?";
        ApprovalOperation = operation;
        ApprovalArguments = arguments;
        _resolve = resolve;
        _approveCommand = new Command(() => Resolve(true), () => !_isResolved);
        _denyCommand = new Command(() => Resolve(false), () => !_isResolved);
    }

    public string Role => "assistant";
    public DateTime CreatedAt { get; }
    public string Timestamp => CreatedAt.ToLocalTime().ToString("g");
    public string Content => string.Empty;
    public bool IsUser => false;
    public bool IsTool => false;
    public string ToolInput => string.Empty;
    public string ToolOutput => string.Empty;
    public string ToolTitle => string.Empty;
    public bool IsToolExpanded => false;
    public string ToolExpansionGlyph => string.Empty;
    public ICommand ToggleToolExpandedCommand { get; } = new Command(() => { });
    public string ApprovalTitle => "Tool approval required";
    public string ApprovalDescription { get; }
    public string ApprovalOperation { get; }
    public string ApprovalArguments { get; }
    public ChatApprovalStatus ApprovalStatus { get; private set; } = ChatApprovalStatus.Pending;
    public ICommand ApproveCommand => _approveCommand;
    public ICommand DenyCommand => _denyCommand;

    private void Resolve(bool approve)
    {
        if (_isResolved) return;
        _isResolved = true;
        ApprovalStatus = approve ? ChatApprovalStatus.Approved : ChatApprovalStatus.Denied;
        OnPropertyChanged(nameof(ApprovalStatus));
        _approveCommand.ChangeCanExecute();
        _denyCommand.ChangeCanExecute();
        _resolve(approve);
    }
}
