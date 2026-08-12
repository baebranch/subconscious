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

    private const int HistoryLimit = 128;

    private bool _programmaticChange;
    private string _lastVisibleText = string.Empty;
    private IReadOnlyList<NativeTextSpan> _textSpans = [];
    private IReadOnlyList<NativeTextSpan> _semanticSpans = [];
    private IReadOnlyList<NativeParagraphSpan> _paragraphSpans = [];
    private IReadOnlyList<NativeTextSpan> _hiddenRanges = [];
    private readonly Dictionary<string, DocumentHistory> _historyByDocumentId = [];

    public event EventHandler<EditorTextChangedEventArgs>? DocumentTextChanged;
    /// <summary>Raised when the editor receives the save shortcut for its active document.</summary>
    public event EventHandler? SaveRequested;
    public event EventHandler<double>? VerticalOffsetChanged;
    internal event EventHandler? PresentationApplied;

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
        var persistedContent = MarkdownRichText.Normalize(document.Content ?? string.Empty);
        _programmaticChange = true;
        try
        {
            DocumentId = document.DocumentId;
            Kind = document.Kind;
            Language = document.Language;
            EditorTheme = theme;
            IsReadOnly = document.IsReadOnly;
            InitializeHistory(document.DocumentId, persistedContent);
            SetPersistedContent(persistedContent);
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

    private void SetPersistedContent(string persistedContent)
    {
        if (Kind == EditorDocumentKind.Markdown)
        {
            var rich = MarkdownRichText.Parse(persistedContent);
            _lastVisibleText = rich.Text;
            _textSpans = rich.TextSpans;
            _semanticSpans = rich.TextSpans.Where(span => span.Link is not null).ToArray();
            _paragraphSpans = rich.ParagraphSpans;
        }
        else
        {
            _lastVisibleText = MarkdownRichText.Normalize(persistedContent);
            _textSpans = [];
            _semanticSpans = [];
            _paragraphSpans = [];
        }
        _hiddenRanges = [];
        Text = _lastVisibleText;
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
        // The native surface is the source of truth for character formatting once a document is
        // loaded. Rebuilding _textSpans from the styles just captured off RichEditBox keeps the
        // model in sync with what is actually rendered, so a later full reformat (theme change,
        // undo/redo restore, tab reselect) reapplies the current formatting instead of a stale
        // snapshot from load time.
        _textSpans = MarkdownRichText.BuildTextSpans(visibleText, styles, _semanticSpans);
        _programmaticChange = true;
        try { Text = visibleText; }
        finally { _programmaticChange = false; }
        var persisted = Kind == EditorDocumentKind.Markdown
            ? MarkdownRichText.Serialize(visibleText, styles, paragraphs, _semanticSpans)
            : visibleText;
        RecordHistory(persisted);
        DocumentTextChanged?.Invoke(this, new EditorTextChangedEventArgs(DocumentId, persisted));
    }

    /// <summary>
    /// Native RichEditBox formatting is presentation-only. User-visible undo/redo is therefore
    /// maintained from persisted document content, not RichEditBox's formatting undo queue.
    /// </summary>
    internal bool TryUndo(out string content) => TryMoveHistory(-1, out content);

    internal bool TryRedo(out string content) => TryMoveHistory(1, out content);

    internal void RestoreHistoryContent(string persistedContent)
    {
        if (string.IsNullOrEmpty(DocumentId)) return;
        persistedContent = MarkdownRichText.Normalize(persistedContent);
        _programmaticChange = true;
        try { SetPersistedContent(persistedContent); }
        finally { _programmaticChange = false; }
        RequestPresentation();
        DocumentTextChanged?.Invoke(this, new EditorTextChangedEventArgs(DocumentId, persistedContent));
    }

    private void InitializeHistory(string documentId, string persistedContent)
    {
        if (string.IsNullOrEmpty(documentId)) return;
        if (!_historyByDocumentId.TryGetValue(documentId, out var history)
            || !string.Equals(history.Current, persistedContent, StringComparison.Ordinal))
        {
            _historyByDocumentId[documentId] = new DocumentHistory(persistedContent);
        }
    }

    private void RecordHistory(string persistedContent)
    {
        if (string.IsNullOrEmpty(DocumentId)) return;
        if (!_historyByDocumentId.TryGetValue(DocumentId, out var history))
        {
            _historyByDocumentId[DocumentId] = new DocumentHistory(persistedContent);
            return;
        }
        if (string.Equals(history.Current, persistedContent, StringComparison.Ordinal)) return;
        if (history.Index < history.Entries.Count - 1)
        {
            history.Entries.RemoveRange(history.Index + 1, history.Entries.Count - history.Index - 1);
        }
        history.Entries.Add(persistedContent);
        history.Index = history.Entries.Count - 1;
        if (history.Entries.Count > HistoryLimit)
        {
            history.Entries.RemoveAt(0);
            history.Index--;
        }
    }

    private bool TryMoveHistory(int offset, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrEmpty(DocumentId)
            || !_historyByDocumentId.TryGetValue(DocumentId, out var history))
        {
            return false;
        }
        var index = history.Index + offset;
        if (index < 0 || index >= history.Entries.Count) return false;
        history.Index = index;
        content = history.Current;
        return true;
    }

    internal void AddSemanticSpan(int start, int length, NativeTextStyle style, string data)
    {
        var spans = _semanticSpans.ToList();
        spans.RemoveAll(item => item.Start < start + length && item.Start + item.Length > start);
        spans.Add(new NativeTextSpan(start, length, style, data));
        _semanticSpans = spans;
    }

    // Paragraphs are non-overlapping half-open-by-construction ranges: paragraph i occupies gap
    // positions [Start, Start+Length] inclusive (Start+Length is "end of this line", the caret
    // position right before its line break), and paragraph i+1 starts at Start+Length+1 (right
    // after that line break). Padding either bound with Math.Max(1, Length) - as this used to do
    // for empty lines - makes an empty paragraph's upper bound reach one position past its own
    // end, which is exactly the next paragraph's Start. That let an emptied heading/list line
    // claim the line below it. Do not pad these containment/overlap comparisons; padding is only
    // valid when constructing an actual (non-degenerate) native text range.
    internal NativeParagraphKind GetParagraphKind(int position)
    {
        var paragraph = _paragraphSpans.FirstOrDefault(item => item.Start <= position
            && item.Start + item.Length >= position);
        return paragraph?.Kind ?? NativeParagraphKind.Normal;
    }

    internal void SetParagraphKind(int start, int length, NativeParagraphKind kind, int alignment = 0)
    {
        var end = start + Math.Max(0, length);
        var paragraphs = _paragraphSpans.Where(item => item.Start + item.Length < start
            || item.Start > end).ToList();
        paragraphs.Add(new NativeParagraphSpan(start, Math.Max(0, length), kind, alignment));
        _paragraphSpans = paragraphs;
    }

    internal void RaiseSaveRequested() => SaveRequested?.Invoke(this, EventArgs.Empty);

    internal void RaiseVerticalOffsetChanged(double offset) => VerticalOffsetChanged?.Invoke(this, offset);

    internal void RaisePresentationApplied() => PresentationApplied?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Returns the lines currently visible, with the positions the platform actually rendered
    /// them at. Returns an empty list where no native line geometry is available.
    /// </summary>
    internal IReadOnlyList<NativeLineMetric> GetVisibleLineMetrics(IReadOnlyList<int> lineStarts)
    {
#if WINDOWS
        if (Handler is Platforms.Windows.NativeDocumentEditorHandler windowsHandler)
        {
            return windowsHandler.GetVisibleLineMetrics(lineStarts);
        }
#endif
        return [];
    }

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
        RecordHistory(persisted);
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

    private sealed class DocumentHistory(string initialContent)
    {
        public List<string> Entries { get; } = [initialContent];
        public int Index { get; set; }
        public string Current => Entries[Index];
    }
}