using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using XamlBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using XamlThickness = Microsoft.UI.Xaml.Thickness;
using XamlHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using XamlVerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using VisualTreeHelper = Microsoft.UI.Xaml.Media.VisualTreeHelper;

namespace Subconscious.WYSIWYG.Platforms.Windows;

/// <summary>WinUI RichEditBox bridge for rendered Markdown and highlighted, foldable code.</summary>
public sealed class NativeDocumentEditorHandler : ViewHandler<NativeDocumentEditor, RichEditBox>
{
    public static readonly IPropertyMapper<NativeDocumentEditor, NativeDocumentEditorHandler> Mapper =
        new PropertyMapper<NativeDocumentEditor, NativeDocumentEditorHandler>(ViewHandler.ViewMapper)
        {
            [nameof(NativeDocumentEditor.Text)] = MapText,
            [nameof(NativeDocumentEditor.IsReadOnly)] = MapReadOnly,
            [nameof(NativeDocumentEditor.EditorTheme)] = MapTheme,
            [nameof(NativeDocumentEditor.Kind)] = MapMode,
            [nameof(NativeDocumentEditor.PresentationRevision)] = MapPresentation,
            [nameof(NativeDocumentEditor.IsEnabled)] = MapEnabled,
        };

    private bool _applying;
    private bool _isLoaded;
    private ScrollViewer? _scrollViewer;

    public NativeDocumentEditorHandler() : base(Mapper) { }

    protected override RichEditBox CreatePlatformView() => new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        IsSpellCheckEnabled = true,
        BorderThickness = new XamlThickness(.5),
        Padding = new XamlThickness(12, 8, 12, 8),
        VerticalAlignment = XamlVerticalAlignment.Stretch,
        HorizontalAlignment = XamlHorizontalAlignment.Stretch,
        HorizontalContentAlignment = XamlHorizontalAlignment.Stretch,
        VerticalContentAlignment = XamlVerticalAlignment.Stretch,
    };

    protected override void ConnectHandler(RichEditBox platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TextChanged += OnTextChanged;
        platformView.Loaded += OnLoaded;
        // Property mapper calls can occur before Loaded. RichEditBox.Document and its text
        // ranges are not safe to access during that phase, so OnLoaded performs the first sync.
    }

    protected override void DisconnectHandler(RichEditBox platformView)
    {
        _isLoaded = false;
        platformView.TextChanged -= OnTextChanged;
        platformView.Loaded -= OnLoaded;
        DetachScrollViewer();
        base.DisconnectHandler(platformView);
    }
    public void ExecuteFormat(string command, string? value)
    {
        if (VirtualView.IsReadOnly) return;
        var selection = PlatformView.Document.Selection;
        var start = selection.StartPosition;
        var end = selection.EndPosition;
        _applying = true;
        try
        {
            switch (command)
            {
                case "bold": selection.CharacterFormat.Bold = Toggle(selection.CharacterFormat.Bold); break;
                case "italic": selection.CharacterFormat.Italic = Toggle(selection.CharacterFormat.Italic); break;
                case "underline":
                    selection.CharacterFormat.Underline = selection.CharacterFormat.Underline == UnderlineType.None
                        ? UnderlineType.Single : UnderlineType.None;
                    break;
                case "code":
                    selection.CharacterFormat.Name = IsMonospace(selection.CharacterFormat.Name) ? "Segoe UI" : "Cascadia Mono";
                    break;
                case "normal": ApplyParagraphFormat(selection, NativeParagraphKind.Normal); break;
                case "heading": ApplyParagraphFormat(selection, NativeParagraphKind.Heading2); break;
                case "ordered-list": ApplyParagraphFormat(selection, NativeParagraphKind.Ordered); break;
                case "bullet-list": ApplyParagraphFormat(selection, NativeParagraphKind.Bullet); break;
                case "align": ApplyAlignment(selection); break;
                case "clear": ClearFormat(selection); break;
                case "link": ApplyLink(selection, value ?? "https://"); break;
                case "image": InsertSemantic(selection, "🖼 Image", $"image:{value ?? "https://"}", NativeTextStyle.Italic); break;
                case "video": InsertSemantic(selection, "▶ Video", $"video:{value ?? "https://"}", NativeTextStyle.Underline); break;
                case "formula": InsertSemantic(selection, "formula", "formula:formula", NativeTextStyle.Code); break;
            }
            selection.SetRange(start, Math.Max(start, end));
        }
        finally
        {
            _applying = false;
        }
        CommitCurrentDocument();
    }

    private void ApplyParagraphFormat(ITextRange selection, NativeParagraphKind kind)
    {
        var range = selection.GetClone();
        range.Expand(TextRangeUnit.Paragraph);
        range.ParagraphFormat.ListType = kind switch
        {
            NativeParagraphKind.Bullet => MarkerType.Bullet,
            NativeParagraphKind.Ordered => MarkerType.Arabic,
            _ => MarkerType.None,
        };
        range.CharacterFormat.Size = kind switch
        {
            NativeParagraphKind.Heading1 => 26,
            NativeParagraphKind.Heading2 => 22,
            NativeParagraphKind.Heading3 => 18,
            _ => 14,
        };
        range.CharacterFormat.Bold = kind is NativeParagraphKind.Heading1
            or NativeParagraphKind.Heading2 or NativeParagraphKind.Heading3 ? FormatEffect.On : FormatEffect.Off;
        VirtualView.SetParagraphKind(range.StartPosition, range.EndPosition - range.StartPosition, kind);
    }

    private void ApplyAlignment(ITextRange selection)
    {
        var range = selection.GetClone();
        range.Expand(TextRangeUnit.Paragraph);
        range.ParagraphFormat.Alignment = range.ParagraphFormat.Alignment == ParagraphAlignment.Center
            ? ParagraphAlignment.Left : ParagraphAlignment.Center;
        VirtualView.SetParagraphKind(range.StartPosition, range.EndPosition - range.StartPosition,
            VirtualView.GetParagraphKind(range.StartPosition), range.ParagraphFormat.Alignment == ParagraphAlignment.Center ? 1 : 0);
    }
    private void ClearFormat(ITextRange selection)
    {
        selection.CharacterFormat.Bold = FormatEffect.Off;
        selection.CharacterFormat.Italic = FormatEffect.Off;
        selection.CharacterFormat.Underline = UnderlineType.None;
        selection.CharacterFormat.Name = "Segoe UI";
        selection.CharacterFormat.Size = 14;
        selection.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Text);
        selection.ParagraphFormat.ListType = MarkerType.None;
        VirtualView.SetParagraphKind(selection.StartPosition, Math.Max(1, selection.Length), NativeParagraphKind.Normal);
    }

    private void ApplyLink(ITextRange selection, string url)
    {
        if (selection.Length == 0)
        {
            var start = selection.StartPosition;
            selection.Text = "link";
            selection.SetRange(start, start + 4);
        }
        selection.CharacterFormat.Underline = UnderlineType.Single;
        selection.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Accent);
        VirtualView.AddSemanticSpan(selection.StartPosition, selection.Length, NativeTextStyle.Underline, url);
    }

    private void InsertSemantic(ITextRange selection, string text, string data, NativeTextStyle style)
    {
        var start = Math.Min(selection.StartPosition, selection.EndPosition);
        selection.Text = text;
        var end = start + text.Length;
        var range = PlatformView.Document.GetRange(start, end);
        if (style.HasFlag(NativeTextStyle.Italic)) range.CharacterFormat.Italic = FormatEffect.On;
        if (style.HasFlag(NativeTextStyle.Underline)) range.CharacterFormat.Underline = UnderlineType.Single;
        if (style.HasFlag(NativeTextStyle.Code)) range.CharacterFormat.Name = "Cascadia Mono";
        VirtualView.AddSemanticSpan(start, text.Length, style, data);
        selection.SetRange(start, end);
    }

    private void OnTextChanged(object sender, RoutedEventArgs args)
    {
        if (!_applying) CommitCurrentDocument();
    }

    private void CommitCurrentDocument()
    {
        if (!_isLoaded || _applying || string.IsNullOrEmpty(VirtualView.DocumentId)) return;
        var text = ReadText();
        var styles = VirtualView.Kind == EditorDocumentKind.Markdown ? CaptureStyles(text) : [];
        var paragraphs = VirtualView.Kind == EditorDocumentKind.Markdown ? CaptureParagraphs(text) : [];
        VirtualView.ReceiveNativeChange(text, styles, paragraphs);
    }

    private IReadOnlyList<NativeTextStyle> CaptureStyles(string text)
    {
        var styles = new NativeTextStyle[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n') continue;
            var format = PlatformView.Document.GetRange(index, index + 1).CharacterFormat;
            if (format.Bold == FormatEffect.On) styles[index] |= NativeTextStyle.Bold;
            if (format.Italic == FormatEffect.On) styles[index] |= NativeTextStyle.Italic;
            if (format.Underline != UnderlineType.None) styles[index] |= NativeTextStyle.Underline;
            if (IsMonospace(format.Name)) styles[index] |= NativeTextStyle.Code;
        }
        return styles;
    }

    private IReadOnlyList<NativeParagraphSpan> CaptureParagraphs(string text)
    {
        var result = new List<NativeParagraphSpan>();
        foreach (var line in MarkdownRichText.GetLines(text))
        {
            var range = PlatformView.Document.GetRange(line.Start, Math.Min(text.Length, line.Start + Math.Max(1, line.Length)));
            var kind = range.ParagraphFormat.ListType switch
            {
                MarkerType.Bullet => NativeParagraphKind.Bullet,
                MarkerType.Arabic => NativeParagraphKind.Ordered,
                _ when range.CharacterFormat.Size >= 24 => NativeParagraphKind.Heading1,
                _ when range.CharacterFormat.Size >= 20 => NativeParagraphKind.Heading2,
                _ when range.CharacterFormat.Size >= 17 => NativeParagraphKind.Heading3,
                _ => VirtualView.GetParagraphKind(line.Start),
            };
            var alignment = range.ParagraphFormat.Alignment == ParagraphAlignment.Center ? 1 : 0;
            result.Add(new NativeParagraphSpan(line.Start, line.Length, kind, alignment));
        }
        return result;
    }
    private void ApplyAll()
    {
        MapReadOnly(this, VirtualView);
        MapMode(this, VirtualView);
        MapTheme(this, VirtualView);
        SynchronizeText();
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        if (!_isLoaded || _applying || PlatformView is null) return;
        var text = ReadText();
        // RichEditBox owns a final paragraph marker internally. Formatting a zero-length range
        // before layout is the WinUI operation that caused the stowed startup exception.
        if (text.Length == 0) return;
        var selection = PlatformView.Document.Selection;
        var start = selection.StartPosition;
        var end = selection.EndPosition;
        _applying = true;
        try
        {
            var all = PlatformView.Document.GetRange(0, text.Length);
            all.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Text);
            all.CharacterFormat.Name = VirtualView.Kind == EditorDocumentKind.Code ? "Cascadia Mono" : "Segoe UI";
            all.CharacterFormat.Size = VirtualView.Kind == EditorDocumentKind.Code ? 13 : 14;
            all.CharacterFormat.Bold = FormatEffect.Off;
            all.CharacterFormat.Italic = FormatEffect.Off;
            all.CharacterFormat.Underline = UnderlineType.None;
            all.CharacterFormat.Hidden = FormatEffect.Off;
            all.ParagraphFormat.ListType = MarkerType.None;

            foreach (var paragraph in VirtualView.ParagraphSpans)
            {
                var range = SafeRange(paragraph.Start, paragraph.Length, text.Length);
                range.ParagraphFormat.ListType = paragraph.Kind switch
                {
                    NativeParagraphKind.Bullet => MarkerType.Bullet,
                    NativeParagraphKind.Ordered => MarkerType.Arabic,
                    _ => MarkerType.None,
                };
                range.ParagraphFormat.Alignment = paragraph.Alignment == 1 ? ParagraphAlignment.Center : ParagraphAlignment.Left;
                if (paragraph.Kind is NativeParagraphKind.Heading1 or NativeParagraphKind.Heading2 or NativeParagraphKind.Heading3)
                {
                    range.CharacterFormat.Bold = FormatEffect.On;
                    range.CharacterFormat.Size = paragraph.Kind switch
                    {
                        NativeParagraphKind.Heading1 => 26,
                        NativeParagraphKind.Heading2 => 22,
                        _ => 18,
                    };
                }
                else if (paragraph.Kind == NativeParagraphKind.Quote)
                {
                    range.CharacterFormat.Italic = FormatEffect.On;
                    range.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.MutedText);
                }
                else if (paragraph.Kind == NativeParagraphKind.CodeBlock)
                {
                    range.CharacterFormat.Name = "Cascadia Mono";
                    range.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Text);
                }
                else if (paragraph.Kind == NativeParagraphKind.HorizontalRule)
                {
                    range.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Divider);
                }
            }

            foreach (var span in VirtualView.TextSpans)
            {
                var range = SafeRange(span.Start, span.Length, text.Length);
                ApplyTextSpan(range, span);
            }
            foreach (var hidden in VirtualView.HiddenRanges)
            {
                SafeRange(hidden.Start, hidden.Length, text.Length).CharacterFormat.Hidden = FormatEffect.On;
            }
            selection.SetRange(Math.Min(start, text.Length), Math.Min(end, text.Length));
        }
        finally
        {
            _applying = false;
        }
    }
    private void ApplyTextSpan(ITextRange range, NativeTextSpan span)
    {
        if (span.Style.HasFlag(NativeTextStyle.Bold)) range.CharacterFormat.Bold = FormatEffect.On;
        if (span.Style.HasFlag(NativeTextStyle.Italic)) range.CharacterFormat.Italic = FormatEffect.On;
        if (span.Style.HasFlag(NativeTextStyle.Underline)) range.CharacterFormat.Underline = UnderlineType.Single;
        if (span.Style.HasFlag(NativeTextStyle.Code)) range.CharacterFormat.Name = "Cascadia Mono";
        range.CharacterFormat.ForegroundColor = span.Link switch
        {
            "syntax:keyword" => ToWindowsColor(VirtualView.EditorTheme.SyntaxKeyword),
            "syntax:string" => ToWindowsColor(VirtualView.EditorTheme.SyntaxString),
            "syntax:number" => ToWindowsColor(VirtualView.EditorTheme.SyntaxNumber),
            "syntax:comment" => ToWindowsColor(VirtualView.EditorTheme.MutedText),
            _ when span.Link is not null => ToWindowsColor(VirtualView.EditorTheme.Accent),
            _ => range.CharacterFormat.ForegroundColor,
        };
    }

    private ITextRange SafeRange(int start, int length, int textLength)
    {
        start = Math.Clamp(start, 0, textLength);
        var end = Math.Clamp(start + Math.Max(0, length), start, textLength);
        return PlatformView.Document.GetRange(start, end);
    }

    private string ReadText()
    {
        PlatformView.Document.GetText(TextGetOptions.None, out var text);
        if (text.EndsWith('\r')) text = text[..^1];
        return MarkdownRichText.Normalize(text);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        DetachScrollViewer();
        _scrollViewer = FindDescendant<ScrollViewer>(PlatformView);
        if (_scrollViewer is not null) _scrollViewer.ViewChanged += OnScrollViewChanged;
        ApplyAll();
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) =>
        VirtualView.RaiseVerticalOffsetChanged(_scrollViewer?.VerticalOffset ?? 0);

    private void DetachScrollViewer()
    {
        if (_scrollViewer is not null) _scrollViewer.ViewChanged -= OnScrollViewChanged;
        _scrollViewer = null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void SynchronizeText()
    {
        if (!_isLoaded || _applying)
        {
            return;
        }

        var desired = MarkdownRichText.Normalize(VirtualView.Text ?? string.Empty);
        if (ReadText() == desired)
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var start = selection.StartPosition;
        var end = selection.EndPosition;
        _applying = true;
        try
        {
            PlatformView.Document.SetText(TextSetOptions.None, desired);
            selection.SetRange(Math.Min(start, desired.Length), Math.Min(end, desired.Length));
        }
        finally
        {
            _applying = false;
        }
    }

    private static void MapText(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        if (handler._applying) return;
        if (!handler._isLoaded)
        {
            return;
        }
        handler.SynchronizeText();
        handler.ApplyPresentation();
    }

    private static void MapReadOnly(NativeDocumentEditorHandler handler, NativeDocumentEditor view) =>
        handler.PlatformView.IsReadOnly = view.IsReadOnly;

    private static void MapEnabled(NativeDocumentEditorHandler handler, NativeDocumentEditor view) =>
        handler.PlatformView.IsEnabled = view.IsEnabled;

    private static void MapMode(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        handler.PlatformView.IsSpellCheckEnabled = view.Kind == EditorDocumentKind.Markdown;
        handler.PlatformView.TextWrapping = view.Kind == EditorDocumentKind.Code ? TextWrapping.NoWrap : TextWrapping.Wrap;
    }

    private static void MapTheme(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        handler.PlatformView.Background = new XamlBrush(ToWindowsColor(view.EditorTheme.Surface));
        handler.PlatformView.Foreground = new XamlBrush(ToWindowsColor(view.EditorTheme.Text));
        handler.PlatformView.BorderBrush = new XamlBrush(ToWindowsColor(view.EditorTheme.Divider));
        handler.PlatformView.SelectionHighlightColor = new XamlBrush(ToWindowsColor(view.EditorTheme.Selection));
    }

    private static void MapPresentation(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        if (!handler._isLoaded)
        {
            return;
        }
        handler.ApplyPresentation();
    }

    private static FormatEffect Toggle(FormatEffect value) => value == FormatEffect.On ? FormatEffect.Off : FormatEffect.On;
    private static bool IsMonospace(string? name) => name?.Contains("Mono", StringComparison.OrdinalIgnoreCase) == true
        || name?.Contains("Consolas", StringComparison.OrdinalIgnoreCase) == true;

    private static global::Windows.UI.Color ToWindowsColor(Color color) => global::Windows.UI.Color.FromArgb(
        (byte)Math.Round(color.Alpha * 255), (byte)Math.Round(color.Red * 255),
        (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255));
}