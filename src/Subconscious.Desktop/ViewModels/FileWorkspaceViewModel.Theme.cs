using CommunityToolkit.Mvvm.ComponentModel;
using Subconscious.WYSIWYG;

namespace Subconscious.Desktop.ViewModels;

public sealed partial class FileWorkspaceViewModel
{
    [ObservableProperty] private EditorTheme _editorTheme = EditorTheme.Light;

    /// <summary>Maps the host's persisted semantic palette into the reusable editor contract.</summary>
    public void RefreshEditorTheme()
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var text = ReadColor(resources, "PrimaryTextColor", EditorTheme.Light.Text);
        var theme = new EditorTheme(
            ReadColor(resources, "SurfaceColor", EditorTheme.Light.Surface),
            ReadColor(resources, "PanelBackgroundColor", EditorTheme.Light.Panel),
            text,
            ReadColor(resources, "SecondaryTextColor", EditorTheme.Light.MutedText),
            ReadColor(resources, "DividerColor", EditorTheme.Light.Divider),
            ReadColor(resources, "HoverColor", EditorTheme.Light.Hover),
            ReadColor(resources, "AccentColor", EditorTheme.Light.Accent),
            ReadColor(resources, "ContextRowSelectedBackgroundColor", EditorTheme.Light.Selection));
        EditorTheme = text.Red + text.Green + text.Blue > 1.5
            ? theme with
            {
                SyntaxKeyword = EditorTheme.Dark.SyntaxKeyword,
                SyntaxString = EditorTheme.Dark.SyntaxString,
                SyntaxNumber = EditorTheme.Dark.SyntaxNumber,
            }
            : theme;
    }

    private static Color ReadColor(ResourceDictionary resources, string key, Color fallback) =>
        resources.TryGetValue(key, out var value) && value is Color color ? color : fallback;
}
