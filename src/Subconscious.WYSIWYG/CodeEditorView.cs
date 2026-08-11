using System.Text.RegularExpressions;

namespace Subconscious.WYSIWYG;

/// <summary>Native themed code editor with line numbers, highlighting, and brace folding.</summary>
public sealed partial class CodeEditorView : Grid
{
    private const double LineHeight = 21;
    private readonly NativeDocumentEditor _editor = new()
    {
        Kind = EditorDocumentKind.Code,
        FontFamily = "Cascadia Mono, Consolas",
        FontSize = 13,
        Placeholder = "Start typing…",
    };
    private readonly VerticalStackLayout _gutter = new() { Spacing = 0, Padding = new Thickness(0, 8, 0, 0) };
    private readonly HashSet<int> _foldedLines = [];
    private IReadOnlyList<FoldBlock> _foldBlocks = [];
    private EditorTheme _theme = EditorTheme.Light;
    private string _source = string.Empty;

    public event EventHandler<EditorTextChangedEventArgs>? DocumentTextChanged;

    public CodeEditorView()
    {
        ColumnDefinitions.Add(new ColumnDefinition(54));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var gutterViewport = new Grid { IsClippedToBounds = true, BackgroundColor = _theme.Panel };
        gutterViewport.Children.Add(_gutter);
        Children.Add(gutterViewport);
        Children.Add(_editor);
        Grid.SetColumn(_editor, 1);
        _editor.DocumentTextChanged += OnDocumentTextChanged;
        _editor.VerticalOffsetChanged += (_, offset) => _gutter.TranslationY = -offset;
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
        RefreshGutter();
    }

    private void OnDocumentTextChanged(object? sender, EditorTextChangedEventArgs args)
    {
        _source = args.Text;
        _foldedLines.Clear();
        RefreshPresentation();
        DocumentTextChanged?.Invoke(this, args);
    }

    private void RefreshPresentation()
    {
        _foldBlocks = FindFoldBlocks(_source);
        _foldedLines.RemoveWhere(line => !_foldBlocks.Any(block => block.Line == line));
        var hidden = _foldBlocks.Where(block => _foldedLines.Contains(block.Line))
            .Select(block => new NativeTextSpan(block.HiddenStart, block.HiddenLength, NativeTextStyle.None))
            .ToArray();
        _editor.SetCodePresentation(Highlight(_source), hidden);
        RefreshGutter();
    }

    private void RefreshGutter()
    {
        _gutter.Children.Clear();
        var lines = MarkdownRichText.GetLines(_source);
        var hiddenLines = new HashSet<int>();
        foreach (var block in _foldBlocks.Where(block => _foldedLines.Contains(block.Line)))
        {
            for (var line = block.Line + 1; line < block.EndLine; line++) hiddenLines.Add(line);
        }
        for (var index = 0; index < lines.Count; index++)
        {
            if (hiddenLines.Contains(index)) continue;
            var block = _foldBlocks.FirstOrDefault(item => item.Line == index);
            var row = new Grid
            {
                HeightRequest = LineHeight,
                ColumnDefinitions = { new ColumnDefinition(20), new ColumnDefinition(GridLength.Star) },
            };
            if (block is not null)
            {
                var line = index;
                var fold = new Button
                {
                    Text = _foldedLines.Contains(index) ? "▸" : "▾",
                    FontSize = 10,
                    Padding = 0,
                    HeightRequest = LineHeight,
                    MinimumHeightRequest = LineHeight,
                    BackgroundColor = Colors.Transparent,
                    TextColor = _theme.MutedText,
                };
                fold.Clicked += (_, _) => ToggleFold(line);
                row.Children.Add(fold);
            }
            var number = new Label
            {
                Text = (index + 1).ToString(),
                FontFamily = "Cascadia Mono, Consolas",
                FontSize = 12,
                TextColor = _theme.MutedText,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(0, 0, 7, 0),
            };
            row.Children.Add(number);
            Grid.SetColumn(number, 1);
            _gutter.Children.Add(row);
        }
        _gutter.BackgroundColor = _theme.Panel;
    }

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
}