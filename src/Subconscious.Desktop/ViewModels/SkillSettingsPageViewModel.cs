using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Owns the session-only Skills page and its independently expandable skill cards.</summary>
public sealed partial class SkillSettingsPageViewModel : ViewModelBase
{
    public ObservableCollection<SkillSettingsFormViewModel> Skills { get; } = [];

    [RelayCommand]
    private void AddSkill()
    {
        Skills.Add(new SkillSettingsFormViewModel(this) { IsExpanded = true });
    }

    internal void Remove(SkillSettingsFormViewModel skill) => Skills.Remove(skill);
}
