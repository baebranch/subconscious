namespace Subconscious.WYSIWYG;

/// <summary>Single-panel native rendered Markdown editor with selection-aware formatting.</summary>
public sealed class MarkdownEditorView : Grid
{
    private readonly NativeDocumentEditor _surface = new()
    {
        Kind = EditorDocumentKind.Markdown,
        Placeholder = "Start writing…",
        FontSize = 13,
    };
    private readonly List<Button> _buttons = [];

    public event EventHandler<EditorTextChangedEventArgs>? DocumentTextChanged;
    public event EventHandler? SaveRequested;

    public MarkdownEditorView()
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        var toolbar = CreateToolbar();
        Children.Add(toolbar);
        Children.Add(_surface);
        Grid.SetRow(_surface, 1);
        _surface.DocumentTextChanged += (_, args) => DocumentTextChanged?.Invoke(this, args);
        _surface.SaveRequested += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    public void LoadDocument(IEditorDocument document, EditorTheme theme) => _surface.LoadDocument(document, theme);
    public void ClearDocument(EditorTheme theme) => _surface.ClearDocument(theme);

    public void ApplyTheme(EditorTheme theme)
    {
        _surface.ApplyTheme(theme);
        BackgroundColor = theme.Surface;
        foreach (var button in _buttons)
        {
            button.BackgroundColor = theme.Surface;
            button.TextColor = theme.Text;
            button.BorderColor = theme.Divider;
            button.BorderWidth = .5;
        }
    }

    private ScrollView CreateToolbar()
    {
        // Keep the toolbar visually separate from the tab strip while preserving the
        // established individual button borders.
        var buttons = new HorizontalStackLayout { Spacing = 3, Padding = new Thickness(0, 5, 0, 7) };
        AddButton(buttons, "Normal", "normal", tooltip: "Normal paragraph");
        AddButton(buttons, "H1", "heading1", tooltip: "Heading 1");
        AddButton(buttons, "H2", "heading2", tooltip: "Heading 2");
        AddButton(buttons, "H3", "heading3", tooltip: "Heading 3");
        AddButton(buttons, "B", "bold", FontAttributes.Bold, "Bold");
        AddButton(buttons, "I", "italic", FontAttributes.Italic, "Italic");
        AddButton(buttons, "U", "underline", tooltip: "Underline");
        AddButton(buttons, "1.", "ordered-list", tooltip: "Numbered list");
        AddButton(buttons, "•", "bullet-list", tooltip: "Bulleted list");
        AddButton(buttons, "≡", "align", tooltip: "Center align paragraph");
        AddButton(buttons, "Link", "link", tooltip: "Insert link");
        AddButton(buttons, "Image", "image", tooltip: "Insert image");
        AddButton(buttons, "Video", "video", tooltip: "Insert video");
        AddButton(buttons, "ƒx", "formula", tooltip: "Insert formula");
        AddButton(buttons, "</>", "code", tooltip: "Inline code");
        AddButton(buttons, "Tx", "clear", tooltip: "Clear formatting");
        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = buttons };
    }

    private void AddButton(Layout buttons, string text, string command,
        FontAttributes attributes = FontAttributes.None, string? tooltip = null)
    {
        var button = new Button
        {
            Text = text,
            CommandParameter = command,
            FontSize = 12,
            FontAttributes = attributes,
            Padding = new Thickness(8, 3),
            MinimumHeightRequest = 30,
            HeightRequest = 30,
        };
        SemanticProperties.SetDescription(button, tooltip ?? command);
        ToolTipProperties.SetText(button, tooltip ?? command);
        button.Clicked += async (_, _) =>
        {
            var value = await PromptForValueAsync(command);
            if (command is not ("link" or "image" or "video") || value is not null)
            {
                await _surface.ExecuteFormatAsync(command, value);
            }
        };
        _buttons.Add(button);
        buttons.Children.Add(button);
    }

    private static async Task<string?> PromptForValueAsync(string command)
    {
        if (command is not ("link" or "image" or "video")) return null;
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return "https://";
        var title = command switch { "image" => "Insert image", "video" => "Insert video", _ => "Insert link" };
        return await page.DisplayPromptAsync(title, "Enter a URL", initialValue: "https://", keyboard: Keyboard.Url);
    }
}