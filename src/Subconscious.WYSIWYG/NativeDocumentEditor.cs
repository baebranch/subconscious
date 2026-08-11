namespace Subconscious.WYSIWYG;

/// <summary>
/// Cross-platform native editor. Windows uses a RichEditBox handler; other targets retain the
/// MAUI Editor fallback while sharing document identity and persistence behavior.
/// </summary>
public sealed class NativeDocumentEditor : Editor
{
    public static readonly BindableProperty DocumentIdProperty = BindableProperty.Create(
        nameof(DocumentId), typeof(string), typeof(NativeDocumentEditor), string.Empty);
    public static readonly BindableProperty KindProperty = BindableProperty.Create(
        nameof(Kind), typeof(EditorDocumentKind), typeof(NativeDocumentEditor), EditorDocumentKind.Text);
    public static readonly BindableProperty LanguageProperty = BindableProperty.Create(
        nameof(Language), typeof(string), typeof(NativeDocumentEditor), string.Empty);
    public static readonly BindableProperty EditorThemeProperty = BindableProperty.Create(
        nameof(EditorTheme), typeof(EditorTheme), typeof(NativeDocumentEditor), EditorTheme.Light,
        propertyChanged: static (view, _, _) => ((NativeDocumentEditor)view).ApplyVisualTheme());
    public static readonly BindableProperty PresentationRevisionProperty = BindableProperty.Create(
        nameof(PresentationRevision), typeof(int), typeof(NativeDocumentEditor), 0);

    private bool _programmaticChange;
    private string _lastVisibleText = string.Empty;
    private IReadOnlyList<NativeTextSpan> _textSpans = [];
    private IReadOnlyList<NativeTextSpan> _semanticSpans = [];
    private IReadOnlyList<NativeParagraphSpan> _paragraphSpans = [];
    private IReadOnlyList<NativeTextSpan> _hiddenRanges = [];

    public event EventHandler<EditorTextChangedEventArgs>? DocumentTextChanged;
    public event EventHandler<double>? VerticalOffsetChanged;

    public string DocumentId
    {
        get => (string)GetValue(DocumentIdProperty);
        set => SetValue(DocumentIdProperty, value);
    }
    public EditorDocumentKind Kind
    {
        get => (EditorDocumentKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
    public string Language
    {
        get => (string)GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }
    public EditorTheme EditorTheme
    {
        get => (EditorTheme)GetValue(EditorThemeProperty);
        set => SetValue(EditorThemeProperty, value);
    }
    public int PresentationRevision
    {
        get => (int)GetValue(PresentationRevisionProperty);
        private set => SetValue(PresentationRevisionProperty, value);
    }
    internal IReadOnlyList<NativeTextSpan> TextSpans => _textSpans;
    internal IReadOnlyList<NativeTextSpan> HiddenRanges => _hiddenRanges;
    internal IReadOnlyList<NativeParagraphSpan> ParagraphSpans => _paragraphSpans;

    public NativeDocumentEditor()
    {
        AutoSize = EditorAutoSizeOption.Disabled;
        TextChanged += OnFallbackTextChanged;
        ApplyVisualTheme();
    }

    public void LoadDocument(IEditorDocument document, EditorTheme theme)
    {
        _programmaticChange = true;
        try
        {
            DocumentId = document.DocumentId;
            Kind = document.Kind;
            Language = document.Language;
            EditorTheme = theme;
            IsReadOnly = document.IsReadOnly;
            if (document.Kind == EditorDocumentKind.Markdown)
            {
                var rich = MarkdownRichText.Parse(document.Content ?? string.Empty);
                _lastVisibleText = rich.Text;
                _textSpans = rich.TextSpans;
                _semanticSpans = rich.TextSpans.Where(span => span.Link is not null).ToArray();
                _paragraphSpans = rich.ParagraphSpans;
            }
            else
            {
                _lastVisibleText = MarkdownRichText.Normalize(document.Content ?? string.Empty);
                _textSpans = [];
                _semanticSpans = [];
                _paragraphSpans = [];
            }
            _hiddenRanges = [];
            Text = _lastVisibleText;
        }
        finally
        {
            _programmaticChange = false;
        }
        RequestPresentation();
    }

    public void ClearDocument(EditorTheme theme)
    {
        _programmaticChange = true;
        try
        {
            DocumentId = string.Empty;
            Kind = EditorDocumentKind.Text;
            Language = string.Empty;
            EditorTheme = theme;
            IsReadOnly = true;
            _lastVisibleText = string.Empty;
            _textSpans = [];
            _semanticSpans = [];
            _paragraphSpans = [];
            _hiddenRanges = [];
            Text = string.Empty;
        }
        finally
        {
            _programmaticChange = false;
        }
        RequestPresentation();
    }

    public void ApplyTheme(EditorTheme theme)
    {
        EditorTheme = theme;
        ApplyVisualTheme();
        RequestPresentation();
    }

    internal void SetCodePresentation(IReadOnlyList<NativeTextSpan> spans, IReadOnlyList<NativeTextSpan> hiddenRanges)
    {
        _textSpans = spans;
        _hiddenRanges = hiddenRanges;
        RequestPresentation();
    }

    public async Task ExecuteFormatAsync(string command, string? value = null)
    {
        if (IsReadOnly || string.IsNullOrEmpty(DocumentId)) return;
#if WINDOWS
        if (Handler is Platforms.Windows.NativeDocumentEditorHandler windowsHandler)
        {
            windowsHandler.ExecuteFormat(command, value);
            return;
        }
#endif
        await Task.CompletedTask;
        ExecuteFallbackFormat(command, value);
    }
    internal void ReceiveNativeChange(string visibleText, IReadOnlyList<NativeTextStyle> styles,
        IReadOnlyList<NativeParagraphSpan> paragraphs)
    {
        if (_programmaticChange || string.IsNullOrEmpty(DocumentId)) return;
        visibleText = MarkdownRichText.Normalize(visibleText);
        _semanticSpans = MarkdownRichText.AdjustSemanticSpans(_semanticSpans, _lastVisibleText, visibleText);
        _lastVisibleText = visibleText;
        _paragraphSpans = paragraphs;
        _programmaticChange = true;
        try { Text = visibleText; }
        finally { _programmaticChange = false; }
        var persisted = Kind == EditorDocumentKind.Markdown
            ? MarkdownRichText.Serialize(visibleText, styles, paragraphs, _semanticSpans)
            : visibleText;
        DocumentTextChanged?.Invoke(this, new EditorTextChangedEventArgs(DocumentId, persisted));
    }

    internal void AddSemanticSpan(int start, int length, NativeTextStyle style, string data)
    {
        var spans = _semanticSpans.ToList();
        spans.RemoveAll(item => item.Start < start + length && item.Start + item.Length > start);
        spans.Add(new NativeTextSpan(start, length, style, data));
        _semanticSpans = spans;
    }

    internal NativeParagraphKind GetParagraphKind(int position)
    {
        var paragraph = _paragraphSpans.FirstOrDefault(item => item.Start <= position
            && item.Start + Math.Max(1, item.Length) >= position);
        return paragraph?.Kind ?? NativeParagraphKind.Normal;
    }

    internal void SetParagraphKind(int start, int length, NativeParagraphKind kind, int alignment = 0)
    {
        var paragraphs = _paragraphSpans.Where(item => item.Start + item.Length < start
            || item.Start > start + Math.Max(1, length)).ToList();
        paragraphs.Add(new NativeParagraphSpan(start, Math.Max(1, length), kind, alignment));
        _paragraphSpans = paragraphs;
    }

    internal void RaiseVerticalOffsetChanged(double offset) => VerticalOffsetChanged?.Invoke(this, offset);

    private void OnFallbackTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (_programmaticChange || string.IsNullOrEmpty(DocumentId))
        {
            return;
        }
#if WINDOWS
        if (Handler is Platforms.Windows.NativeDocumentEditorHandler)
        {
            return;
        }
#endif
        var text = MarkdownRichText.Normalize(args.NewTextValue ?? string.Empty);
        _semanticSpans = MarkdownRichText.AdjustSemanticSpans(_semanticSpans, _lastVisibleText, text);
        _lastVisibleText = text;
        var styles = Enumerable.Repeat(NativeTextStyle.None, text.Length).ToArray();
        foreach (var span in _textSpans)
        {
            for (var index = Math.Max(0, span.Start); index < Math.Min(text.Length, span.Start + span.Length); index++)
                styles[index] |= span.Style;
        }
        var persisted = Kind == EditorDocumentKind.Markdown
            ? MarkdownRichText.Serialize(text, styles, _paragraphSpans, _semanticSpans)
            : text;
        DocumentTextChanged?.Invoke(this, new EditorTextChangedEventArgs(DocumentId, persisted));
    }

    private void ExecuteFallbackFormat(string command, string? value)
    {
        var text = Text ?? string.Empty;
        var start = Math.Clamp(CursorPosition, 0, text.Length);
        var length = Math.Clamp(SelectionLength, 0, text.Length - start);
        if (length == 0 && command is "image" or "video" or "formula")
        {
            var inserted = command switch { "image" => "🖼 Image", "video" => "▶ Video", _ => "formula" };
            Text = text.Insert(start, inserted);
            length = inserted.Length;
            SelectionLength = length;
        }
        var style = command switch
        {
            "bold" => NativeTextStyle.Bold,
            "italic" => NativeTextStyle.Italic,
            "underline" => NativeTextStyle.Underline,
            "code" => NativeTextStyle.Code,
            _ => NativeTextStyle.None,
        };
        if (style != NativeTextStyle.None && length > 0)
        {
            _textSpans = _textSpans.Append(new NativeTextSpan(start, length, style)).ToArray();
        }
        if (command is "link" or "image" or "video" or "formula")
        {
            var data = command switch
            {
                "image" => $"image:{value ?? "https://"}",
                "video" => $"video:{value ?? "https://"}",
                "formula" => "formula:formula",
                _ => value ?? "https://",
            };
            AddSemanticSpan(start, length, style, data);
        }
        RequestPresentation();
    }

    private void ApplyVisualTheme()
    {
        BackgroundColor = EditorTheme.Surface;
        TextColor = EditorTheme.Text;
        PlaceholderColor = EditorTheme.MutedText;
    }

    private void RequestPresentation() => PresentationRevision++;
}