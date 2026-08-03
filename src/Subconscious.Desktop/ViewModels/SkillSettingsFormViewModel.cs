using CommunityToolkit.Mvvm.ComponentModel;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Holds editable Skill Registry fields; validation-derived values remain read-only.</summary>
public sealed partial class SkillSettingsFormViewModel : ViewModelBase
{
    public IReadOnlyList<string> SourceTypeOptions { get; } = ["folder", "zip", "url"];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _alias = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private string _sourceType = "folder";

    /// <summary>Set by future engine validation, never directly selected by the user.</summary>
    [ObservableProperty]
    private string _status = "pending";

    /// <summary>JSON list derived from a validated skill manifest.</summary>
    [ObservableProperty]
    private string _requiredTools = string.Empty;

    public bool IsValidated => Status == "valid";

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(IsValidated));
}
