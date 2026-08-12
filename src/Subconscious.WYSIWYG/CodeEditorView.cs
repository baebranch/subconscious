using System.Text.RegularExpressions;

namespace Subconscious.WYSIWYG;

/// <summary>Native themed code editor with line numbers and syntax highlighting.</summary>
public sealed partial class CodeEditorView : Grid
{
    private const double FallbackLineHeight = 17;
    private const double FallbackContentTop = 8;
    private const double GutterWidth = 42;
    private const int BackgroundHighlightThreshold = 8_000;
    // Retained while folding is paused; no fold controls or ranges are currently rendered.
    private const string ChevronFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private readonly NativeDocumentEditor _editor = new()
    {
        Kind = EditorDocumentKind.Code,
        FontFamily = "Cascadia Mono, Consolas",
        FontSize = 13,
        Placeholder = "Start typing…",
    };
    private readonly Grid _gutter = new();
    private readonly List<GutterRow> _rows = [];
    private readonly HashSet<int> _foldedLines = [];
    private IReadOnlyList<FoldBlock> _foldBlocks = [];
    private IReadOnlyList<NativeTextSpan> _highlightSpans = [];
    private int[] _lineStarts = [0];
    private EditorTheme _theme = EditorTheme.Light;
    private string _source = string.Empty;
    private string? _analyzedSource;
    private double _verticalOffset;
    private CancellationTokenSource? _highlightCancellation;
    private int _highlightRevision;

    public event EventHandler<EditorTextChangedEventArgs>? DocumentTextChanged;
    public event EventHandler? SaveRequested;

    public CodeEditorView()
    {
        ColumnDefinitions.Add(new ColumnDefinition(GutterWidth));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var gutterViewport = new Grid { IsClippedToBounds = true, BackgroundColor = _theme.Panel };
        gutterViewport.Children.Add(_gutter);
        Children.Add(gutterViewport);
        Children.Add(_editor);
        Grid.SetColumn(_editor, 1);
        _editor.DocumentTextChanged += OnDocumentTextChanged;
        _editor.SaveRequested += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        // The gutter follows the native document's own line rectangles, so it is refreshed
        // whenever the document scrolls, is reformatted, or changes size.
        _editor.VerticalOffsetChanged += (_, offset) =>
        {
            _verticalOffset = offset;
            UpdateGutter();
        };
        _editor.PresentationApplied += (_, _) => UpdateGutter();
        _editor.SizeChanged += (_, _) => UpdateGutter();
    }

    public void LoadDocument(IEditorDocument document, EditorTheme theme)
    {
        _theme = theme;
        _source = MarkdownRichText.Normalize(document.Content ?? string.Empty);
        _foldedLines.Clear();
        _editor.LoadDocument(document, theme);
        RefreshPresentation();
    }

    public void ClearDocument(EditorTheme theme)
    {
        _theme = theme;
        _source = string.Empty;
        _foldedLines.Clear();
        _editor.ClearDocument(theme);
        RefreshPresentation();
    }

    public void ApplyTheme(EditorTheme theme)
    {
        _theme = theme;
        BackgroundColor = theme.Surface;
        _editor.ApplyTheme(theme);
        foreach (var row in _rows)
        {
            row.Number.TextColor = theme.MutedText;
        }
        UpdateGutter();
    }

    private void OnDocumentTextChanged(object? sender, EditorTextChangedEventArgs args)
    {
        _source = args.Text;
        RefreshPresentation();
        DocumentTextChanged?.Invoke(this, args);
    }

    private void RefreshPresentation()
    {
        // Folding is deliberately dormant for now. Keep the brace-pairing implementation below
        // for future work, but neither analyze ranges nor hide lines while it is paused.
        _foldedLines.Clear();
        if (!string.Equals(_analyzedSource, _source, StringComparison.Ordinal))
        {
            _analyzedSource = _source;
            _lineStarts = BuildLineStarts(_source);
            StartHighlightAnalysis(_source);
        }
        else
        {
            _editor.SetCodePresentation(_highlightSpans, []);
        }
        UpdateGutter();
    }

    private void StartHighlightAnalysis(string source)
    {
        CancelHighlightAnalysis();
        if (source.Length < BackgroundHighlightThreshold)
        {
            _highlightSpans = Highlight(source);
            _editor.SetCodePresentation(_highlightSpans, []);
            return;
        }

        // Large documents become interactive as plain text first. Lexical analysis is the only
        // full-source work and runs off-thread; the native handler installs its result atomically.
        _highlightSpans = [];
        var cancellation = new CancellationTokenSource();
        _highlightCancellation = cancellation;
        var revision = ++_highlightRevision;
        _ = AnalyzeHighlightAsync(source, revision, cancellation.Token);
    }

    private async Task AnalyzeHighlightAsync(string source, int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(75, cancellationToken).ConfigureAwait(false);
            var spans = await Task.Run(() => Highlight(source), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Dispatcher.Dispatch(() =>
            {
                if (revision != _highlightRevision
                    || !string.Equals(source, _source, StringComparison.Ordinal))
                {
                    return;
                }
                _highlightSpans = spans;
                _editor.SetCodePresentation(spans, []);
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelHighlightAnalysis()
    {
        _highlightRevision++;
        var cancellation = Interlocked.Exchange(ref _highlightCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private static int[] BuildLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n') starts.Add(index + 1);
        }
        return starts.ToArray();
    }

    /// <summary>
    /// Renders only the lines currently in view, positioned at the line rectangles reported by
    /// the native document. A fixed line-height estimate drifted out of alignment, and building
    /// one visual row per line made large files expensive to load and scroll.
    /// </summary>
    private void UpdateGutter()
    {
        var metrics = _editor.GetVisibleLineMetrics(_lineStarts);
        if (metrics.Count == 0)
        {
            metrics = EstimateVisibleLines();
        }

        for (var index = 0; index < metrics.Count; index++)
        {
            var metric = metrics[index];
            var row = EnsureRow(index);
            row.Line = metric.Line;
            row.Root.HeightRequest = metric.Height;
            row.Root.Margin = new Thickness(0, metric.Top, 0, 0);
            row.Root.IsVisible = true;
            row.Number.Text = (metric.Line + 1).ToString();
            row.Number.TextColor = _theme.MutedText;
        }

        for (var index = metrics.Count; index < _rows.Count; index++)
        {
            _rows[index].Root.IsVisible = false;
        }
        _gutter.BackgroundColor = _theme.Panel;
    }

    /// <summary>
    /// Fallback for surfaces that expose no native line geometry. It still honors the reported
    /// scroll offset so the numbers track the document instead of freezing at line one.
    /// </summary>
    private IReadOnlyList<NativeLineMetric> EstimateVisibleLines()
    {
        var height = Height > 0 ? Height : 0;
        if (height <= 0 || _lineStarts.Length == 0) return [];
        var first = Math.Clamp(
            (int)Math.Floor(Math.Max(0, _verticalOffset - FallbackContentTop) / FallbackLineHeight),
            0, _lineStarts.Length - 1);
        var visible = (int)Math.Ceiling(height / FallbackLineHeight) + 1;
        var metrics = new List<NativeLineMetric>(visible);
        for (var line = first; line < _lineStarts.Length && metrics.Count < visible; line++)
        {
            var top = FallbackContentTop + (line * FallbackLineHeight) - _verticalOffset;
            if (top > height) break;
            metrics.Add(new NativeLineMetric(line, top, FallbackLineHeight));
        }
        return metrics;
    }

    private GutterRow EnsureRow(int index)
    {
        if (index < _rows.Count) return _rows[index];
        var root = new Grid
        {
            VerticalOptions = LayoutOptions.Start,
        };
        var number = new Label
        {
            FontFamily = "Cascadia Mono, Consolas",
            FontSize = 12,
            TextColor = _theme.MutedText,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, 0, 6, 0),
        };
        root.Children.Add(number);
        var row = new GutterRow(root, number);
        _gutter.Children.Add(root);
        _rows.Add(row);
        return row;
    }

    private void ToggleRowFold(GutterRow row) => ToggleFold(row.Line);

    private void ToggleFold(int line)
    {
        if (!_foldedLines.Add(line)) _foldedLines.Remove(line);
        RefreshPresentation();
    }

    private static IReadOnlyList<NativeTextSpan> Highlight(string source)
    {
        var spans = new List<NativeTextSpan>();
        foreach (Match match in TokenRegex().Matches(source))
        {
            var token = match.Groups["comment"].Success ? "comment"
                : match.Groups["string"].Success ? "string"
                : match.Groups["number"].Success ? "number" : "keyword";
            spans.Add(new NativeTextSpan(match.Index, match.Length,
                token == "comment" ? NativeTextStyle.Italic : NativeTextStyle.None, $"syntax:{token}"));
        }
        return spans;
    }

    /// <summary>
    /// Fold ranges come from brace pairing: every '{' is pushed with its line, and the matching
    /// '}' closes it. A block is foldable only when it spans more than one line, and the hidden
    /// range covers the lines between the opening line and the closing line.
    /// </summary>
    private static IReadOnlyList<FoldBlock> FindFoldBlocks(string source)
    {
        var result = new List<FoldBlock>();
        var stack = new Stack<(int Position, int Line)>();
        var line = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n') { line++; continue; }
            if (source[index] == '{') stack.Push((index, line));
            else if (source[index] == '}' && stack.Count > 0)
            {
                var open = stack.Pop();
                if (line <= open.Line) continue;
                var hiddenStart = source.IndexOf('\n', open.Position);
                var closingLineStart = source.LastIndexOf('\n', index);
                if (hiddenStart >= 0 && closingLineStart >= hiddenStart)
                {
                    result.Add(new FoldBlock(open.Line, line, hiddenStart + 1,
                        Math.Max(0, closingLineStart - hiddenStart)));
                }
            }
        }
        return result.OrderBy(block => block.Line).ToArray();
    }

    [GeneratedRegex("(?<comment>//[^\\n]*|(?m)^\\s*#[^\\n]*)|(?<string>\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*')|(?<keyword>\\b(?:abstract|as|async|await|bool|break|case|catch|class|const|continue|default|do|else|enum|export|extends|false|finally|for|foreach|from|function|if|import|in|interface|internal|let|namespace|new|null|override|private|protected|public|readonly|record|return|static|string|struct|switch|this|throw|true|try|typeof|using|var|virtual|void|while)\\b)|(?<number>\\b\\d+(?:\\.\\d+)?\\b)", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    private sealed record FoldBlock(int Line, int EndLine, int HiddenStart, int HiddenLength);

    private sealed class GutterRow(Grid root, Label number)
    {
        public Grid Root { get; } = root;
        public Label Number { get; } = number;
        public Button? Fold { get; private set; }
        public int Line { get; set; }

        public Button EnsureFold(EditorTheme theme, Action<GutterRow> toggle)
        {
            if (Fold is not null) return Fold;
            Fold = new Button
            {
                FontFamily = ChevronFont,
                FontSize = 9,
                Padding = 0,
                BorderWidth = 0,
                CornerRadius = 0,
                BackgroundColor = Colors.Transparent,
                TextColor = theme.MutedText,
                VerticalOptions = LayoutOptions.Fill,
                HorizontalOptions = LayoutOptions.Fill,
            };
            SemanticProperties.SetDescription(Fold, "Expand or collapse code block");
            ToolTipProperties.SetText(Fold, "Expand or collapse code block");
            Fold.Clicked += (_, _) => toggle(this);
            Root.Children.Add(Fold);
            Grid.SetColumn(Fold, 1);
            return Fold;
        }
    }
}
