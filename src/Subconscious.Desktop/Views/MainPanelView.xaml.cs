namespace Subconscious.Desktop.Views;

/// <summary>The center utility panel. Inherits MainPage's BindingContext and constrains forms
/// and settings pages to the available center-panel width, up to a readable 750px content width.</summary>
public partial class MainPanelView : ContentView
{
    private const double MaxFormContentWidth = 750;
    private const double FormHorizontalPadding = 24;

    public MainPanelView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateFormHostWidths();
    }

    private void UpdateFormHostWidths()
    {
        // The host includes its 24px gutters on both sides. It is explicitly sized because a
        // centered StackLayout otherwise measures only to its widest child; this keeps every
        // workspace form and settings page equally wide and responsive as panels are dragged.
        var hostWidth = Math.Min(Width, MaxFormContentWidth + FormHorizontalPadding * 2);
        if (hostWidth <= 0)
        {
            return;
        }

        WorkspaceFormHost.WidthRequest = hostWidth;
        SettingsFormHost.WidthRequest = hostWidth;
        ModelsSettingsHost.WidthRequest = hostWidth;
        ToolsSettingsHost.WidthRequest = hostWidth;
        SkillsSettingsHost.WidthRequest = hostWidth;
        AboutSettingsHost.WidthRequest = hostWidth;
    }
}
