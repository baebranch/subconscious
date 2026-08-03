using CommunityToolkit.Mvvm.Input;

namespace Subconscious.Desktop.ViewModels;

/// <summary>Presentation and external-link actions for the About settings page.</summary>
public sealed partial class AboutSettingsViewModel : ViewModelBase
{
    public string Version => AppInfo.Current.VersionString;

    [RelayCommand]
    private Task OpenWebsiteAsync() => Launcher.Default.OpenAsync(new Uri("https://subconscious.chat/"));

    [RelayCommand]
    private Task OpenLicenseAsync() => Launcher.Default.OpenAsync(
        new Uri("https://github.com/Ancilla-Company/Subconscious/blob/main/LICENSE"));

    [RelayCommand]
    private Task ReportIssueAsync() => Launcher.Default.OpenAsync(
        new Uri("https://github.com/Ancilla-Company/Subconscious/issues"));
}
