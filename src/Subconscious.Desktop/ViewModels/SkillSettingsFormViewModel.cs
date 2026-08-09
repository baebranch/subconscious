using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Subconscious.Desktop.ViewModels;

/// <summary>One session-only Skill Registry card and its validation-derived fields.</summary>
public sealed partial class SkillSettingsFormViewModel : ViewModelBase
{
    private readonly SkillSettingsPageViewModel? _owner;

    public SkillSettingsFormViewModel()
    {
    }

    internal SkillSettingsFormViewModel(SkillSettingsPageViewModel owner) => _owner = owner;

    public IReadOnlyList<string> SourceTypeOptions { get; } = ["folder", "zip", "url"];
    public string DisplayName => string.IsNullOrWhiteSpace(Alias)
        ? string.IsNullOrWhiteSpace(Name) ? "New skill" : Name
        : Alias;
    public string Summary => string.Join(" · ", new[] { SourceType, Source }.Where(value => !string.IsNullOrWhiteSpace(value)));

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private string _sourceType = "folder";
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Set by future engine validation, never directly selected by the user.</summary>
    [ObservableProperty] private string _status = "pending";

    /// <summary>JSON list derived from a validated skill manifest.</summary>
    [ObservableProperty] private string _requiredTools = string.Empty;

    public bool IsValidated => Status == "valid";

    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;
    [RelayCommand] private void Remove() => _owner?.Remove(this);

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(IsValidated));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnAliasChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnSourceChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnSourceTypeChanged(string value) => OnPropertyChanged(nameof(Summary));
}
