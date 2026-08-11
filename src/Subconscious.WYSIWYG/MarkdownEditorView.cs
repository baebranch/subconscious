namespace Subconscious.WYSIWYG;

/// <summary>Single-panel native rendered Markdown editor with selection-aware formatting.</summary>
public sealed class MarkdownEditorView : Grid
{
    private readonly NativeDocumentEditor _surface = new()
    {
        Kind = EditorDocumentKind.Markdown,
        Placeholder = "Start writing…",
        FontSize = 14,
    };
    private readonly List<Button> _buttons = [];

    public event EventHandler<EditorTextChangedEventArgs>? DocumentTextChanged;

    public MarkdownEditorView()
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        var toolbar = CreateToolbar();
        Children.Add(toolbar);
        Children.Add(_surface);
        Grid.SetRow(_surface, 1);
        _surface.DocumentTextChanged += (_, args) => DocumentTextChanged?.Invoke(this, args);
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
        var buttons = new HorizontalStackLayout { Spacing = 3, Padding = new Thickness(0, 0, 0, 7) };
        AddButton(buttons, "Normal", "normal"); AddButton(buttons, "H", "heading");
        AddButton(buttons, "B", "bold", FontAttributes.Bold); AddButton(buttons, "I", "italic", FontAttributes.Italic);
        AddButton(buttons, "U", "underline"); AddButton(buttons, "1.", "ordered-list");
        AddButton(buttons, "•", "bullet-list"); AddButton(buttons, "≡", "align");
        AddButton(buttons, "Link", "link"); AddButton(buttons, "Image", "image");
        AddButton(buttons, "Video", "video"); AddButton(buttons, "ƒx", "formula");
        AddButton(buttons, "</>", "code"); AddButton(buttons, "Tx", "clear");
        return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = buttons };
    }

    private void AddButton(Layout buttons, string text, string command, FontAttributes attributes = FontAttributes.None)
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