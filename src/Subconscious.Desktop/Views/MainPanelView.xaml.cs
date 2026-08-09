using Subconscious.Desktop.Controls;

namespace Subconscious.Desktop.Views;

/// <summary>The center utility panel. Forms use a constrained responsive host; the file editor
/// adds native-source Markdown formatting that operates on the current text selection.</summary>
public partial class MainPanelView : ContentView
{
    private const double MaxFormContentWidth = 750;
    private const double FormHorizontalPadding = 24;

    public MainPanelView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateFormHostWidths();
    }

    private void OnMarkdownFormatClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is not Button { CommandParameter: string format })
        {
            return;
        }

        ApplyMarkdownFormat(MarkdownSourceEditor, format);
    }

    private static void ApplyMarkdownFormat(MarkdownEditor editor, string format)
    {
        var text = editor.Text ?? string.Empty;
        var start = Math.Clamp(editor.CursorPosition, 0, text.Length);
        var length = Math.Clamp(editor.SelectionLength, 0, text.Length - start);

        switch (format)
        {
            case "bold": WrapSelection(editor, text, start, length, "**", "**", "bold text"); break;
            case "italic": WrapSelection(editor, text, start, length, "*", "*", "italic text"); break;
            case "underline": WrapSelection(editor, text, start, length, "<u>", "</u>", "underlined text"); break;
            case "link": WrapSelection(editor, text, start, length, "[", "](https://)", "link text"); break;
            case "image": ReplaceSelection(editor, text, start, length, "![alt text](https://image-url)", 2, 8); break;
            case "video": ReplaceSelection(editor, text, start, length, "<video controls src=\"https://video-url\"></video>", 21, 17); break;
            case "formula": WrapSelection(editor, text, start, length, "$", "$", "formula"); break;
            case "code": WrapSelection(editor, text, start, length, "`", "`", "code"); break;
            case "heading": PrefixCurrentLine(editor, text, start, "## "); break;
            case "ordered-list": PrefixLines(editor, text, start, length, "1. "); break;
            case "bullet-list": PrefixLines(editor, text, start, length, "- "); break;
            case "align": WrapSelection(editor, text, start, length, "<div align=\"center\">\n", "\n</div>", "centered text"); break;
            case "normal":
            case "clear": ClearFormatting(editor, text, start, length); break;
        }

        editor.Focus();
    }

    private static void WrapSelection(MarkdownEditor editor, string text, int start, int length, string prefix, string suffix, string placeholder)
    {
        var selected = length == 0 ? placeholder : text.Substring(start, length);
        ReplaceSelection(editor, text, start, length, $"{prefix}{selected}{suffix}", prefix.Length, selected.Length);
    }

    private static void ReplaceSelection(MarkdownEditor editor, string text, int start, int length, string replacement, int selectionOffset, int selectionLength)
    {
        editor.Text = text.Remove(start, length).Insert(start, replacement);
        editor.CursorPosition = start + selectionOffset;
        editor.SelectionLength = selectionLength;
    }

    private static void PrefixCurrentLine(MarkdownEditor editor, string text, int cursor, string prefix)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, cursor - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        editor.Text = text.Insert(lineStart, prefix);
        editor.CursorPosition = cursor + prefix.Length;
        editor.SelectionLength = 0;
    }

    private static void PrefixLines(MarkdownEditor editor, string text, int start, int length, string prefix)
    {
        var selection = length == 0 ? "list item" : text.Substring(start, length);
        var formatted = string.Join(Environment.NewLine, selection.Split('\n').Select(line => $"{prefix}{line}"));
        editor.Text = text.Remove(start, length).Insert(start, formatted);
        editor.CursorPosition = start + prefix.Length;
        editor.SelectionLength = formatted.Length - prefix.Length;
    }

    private static void ClearFormatting(MarkdownEditor editor, string text, int start, int length)
    {
        if (length == 0)
        {
            start = 0;
            length = text.Length;
        }

        var selected = text.Substring(start, length);
        var cleared = selected
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("<u>", string.Empty, StringComparison.Ordinal)
            .Replace("</u>", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
        ReplaceSelection(editor, text, start, length, cleared, 0, cleared.Length);
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
