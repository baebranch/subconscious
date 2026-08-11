namespace Subconscious.Desktop.Views;

/// <summary>The center utility panel. File editing is composed through <see cref="FileEditorView"/>;
/// this view retains only the responsive workspace and settings form host behavior.</summary>
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
