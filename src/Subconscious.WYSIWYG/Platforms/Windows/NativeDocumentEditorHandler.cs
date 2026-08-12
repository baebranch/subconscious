using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using XamlBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using XamlThickness = Microsoft.UI.Xaml.Thickness;
using XamlHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using XamlVerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using VisualTreeHelper = Microsoft.UI.Xaml.Media.VisualTreeHelper;
using XamlMenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using XamlMenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using XamlMenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;

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

    private static readonly global::Windows.UI.Color TransparentColor = global::Windows.UI.Color.FromArgb(0, 0, 0, 0);
    // Windows.System.VirtualKey omits symbolic OEM bracket members; use their Win32 virtual-key values.
    private const global::Windows.System.VirtualKey OpenBracketKey = (global::Windows.System.VirtualKey)219;
    private const global::Windows.System.VirtualKey CloseBracketKey = (global::Windows.System.VirtualKey)221;

    // Brush instances are reused for every visual state. The RichEditBox template swaps
    // Foreground/Background/BorderBrush on PointerOver and Focused; assigning a *different*
    // brush resets the document's character formatting, which is what erased syntax colors on
    // hover. Sharing one instance per role means the state change is a no-op for the document.
    private readonly XamlBrush _foregroundBrush = new(TransparentColor);
    private readonly XamlBrush _backgroundBrush = new(TransparentColor);
    private readonly XamlBrush _borderBrush = new(TransparentColor);
    private readonly XamlBrush _selectionBrush = new(TransparentColor);
    private bool _applying;
    private bool _isLoaded;
    private bool _updatingInsertionFormat;
    private bool _propagatingNativeTextChange;
    private bool _presentationQueued;
    private bool _scrollRefreshQueued;
    private ContentDialog? _findDialog;
    private CancellationTokenSource? _codePresentationCancellation;
    private int _codePresentationRevision;
    private ScrollViewer? _scrollViewer;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _pointerWheelChangedHandler;
    private double _lineHeight;

    public NativeDocumentEditorHandler() : base(Mapper) { }

    protected override RichEditBox CreatePlatformView()
    {
        var platformView = new RichEditBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsSpellCheckEnabled = true,
            BorderThickness = new XamlThickness(0),
            FocusVisualPrimaryThickness = new XamlThickness(0),
            FocusVisualSecondaryThickness = new XamlThickness(0),
            FocusVisualPrimaryBrush = _borderBrush,
            FocusVisualSecondaryBrush = _borderBrush,
            UseSystemFocusVisuals = false,
            IsTabStop = true,
            Padding = new XamlThickness(12, 8, 12, 8),
            VerticalAlignment = XamlVerticalAlignment.Stretch,
            HorizontalAlignment = XamlHorizontalAlignment.Stretch,
            HorizontalContentAlignment = XamlHorizontalAlignment.Stretch,
            VerticalContentAlignment = XamlVerticalAlignment.Top,
            Background = _backgroundBrush,
            Foreground = _foregroundBrush,
            BorderBrush = _borderBrush,
            SelectionHighlightColor = _selectionBrush,
            // The built-in selection flyout is the oversized Bold/Italic/Underline bar that
            // appeared on every selection, sometimes far from the pointer. Editing commands are
            // offered on right-click instead.
            SelectionFlyout = null,
        };
        OverrideTextControlResources(platformView);
        return platformView;
    }

    /// <summary>
    /// Compact right-click menu. Clipboard and selection commands are always present; text
    /// formatting only applies to rendered Markdown, so it is offered only for that kind.
    /// </summary>
    private void BuildContextFlyout()
    {
        var flyout = new XamlMenuFlyout();
        var editable = !VirtualView.IsReadOnly;
        if (VirtualView.Kind == EditorDocumentKind.Markdown && editable)
        {
            AddFlyoutItem(flyout, "Bold", () => ExecuteFormat("bold", null));
            AddFlyoutItem(flyout, "Italic", () => ExecuteFormat("italic", null));
            AddFlyoutItem(flyout, "Underline", () => ExecuteFormat("underline", null));
            AddFlyoutItem(flyout, "Inline code", () => ExecuteFormat("code", null));
            AddFlyoutItem(flyout, "Clear formatting", () => ExecuteFormat("clear", null));
            flyout.Items.Add(new XamlMenuFlyoutSeparator());
        }
        AddFlyoutItem(flyout, "Cut", Cut, editable);
        AddFlyoutItem(flyout, "Copy", Copy);
        AddFlyoutItem(flyout, "Paste", Paste, editable);
        flyout.Items.Add(new XamlMenuFlyoutSeparator());
        AddFlyoutItem(flyout, "Select all", SelectAll);
        PlatformView.ContextFlyout = flyout;
    }

    private static void AddFlyoutItem(XamlMenuFlyout flyout, string text, Action invoke, bool enabled = true)
    {
        // Sized to read like the Markdown toolbar rather than the large command bar buttons.
        var item = new XamlMenuFlyoutItem
        {
            Text = text,
            FontSize = 12,
            MinHeight = 28,
            Padding = new XamlThickness(11, 3, 11, 5),
            IsEnabled = enabled,
        };
        item.Click += (_, _) => invoke();
        flyout.Items.Add(item);
    }

    private void Cut()
    {
        if (CanAccessDocument() && !VirtualView.IsReadOnly) PlatformView.Document.Selection.Cut();
    }

    private void Copy()
    {
        if (CanAccessDocument()) PlatformView.Document.Selection.Copy();
    }

    private void Paste()
    {
        if (CanAccessDocument() && !VirtualView.IsReadOnly) PlatformView.Document.Selection.Paste(0);
    }

    private void SelectAll()
    {
        if (!CanAccessDocument()) return;
        PlatformView.Document.Selection.SetRange(0, ReadText().Length + 1);
    }

    /// <summary>
    /// Replaces the per-state template resources so no state paints a border, the focus
    /// underline, or a different text brush.
    /// </summary>
    private void OverrideTextControlResources(RichEditBox platformView)
    {
        var zero = new XamlThickness(0);
        foreach (var key in new[]
        {
            "TextControlForeground", "TextControlForegroundPointerOver",
            "TextControlForegroundFocused", "TextControlForegroundDisabled",
        })
        {
            platformView.Resources[key] = _foregroundBrush;
        }
        foreach (var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver",
            "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
        })
        {
            platformView.Resources[key] = _backgroundBrush;
        }
        foreach (var key in new[]
        {
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
            "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
        })
        {
            platformView.Resources[key] = _borderBrush;
        }
        platformView.Resources["TextControlBorderThemeThickness"] = zero;
        platformView.Resources["TextControlBorderThemeThicknessFocused"] = zero;
        platformView.Resources["TextControlSelectionHighlightColor"] = _selectionBrush;
        platformView.Resources["TextControlSelectionHighlightColorWhenNotFocused"] = _selectionBrush;
    }

    protected override void ConnectHandler(RichEditBox platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TextChanged += OnTextChanged;
        platformView.Loaded += OnLoaded;
        platformView.LayoutUpdated += OnPlatformLayoutUpdated;
        platformView.PreviewKeyDown += OnPreviewKeyDown;
        platformView.SelectionChanged += OnSelectionChanged;
        _pointerWheelChangedHandler = OnPointerWheelChanged;
        platformView.AddHandler(UIElement.PointerWheelChangedEvent, _pointerWheelChangedHandler, true);
        // Undo/redo is intercepted in PreviewKeyDown rather than through KeyboardAccelerators:
        // WinUI renders an automatic "Ctrl+Z" accelerator tooltip over the editor on hover.
        // Property mapper calls can occur before Loaded. RichEditBox.Document and its text
        // ranges are not safe to access during that phase, so OnLoaded performs the first sync.
    }

    protected override void DisconnectHandler(RichEditBox platformView)
    {
        _isLoaded = false;
        _presentationQueued = false;
        _scrollRefreshQueued = false;
        _lineHeight = 0;
        CancelCodePresentation();
        platformView.TextChanged -= OnTextChanged;
        platformView.Loaded -= OnLoaded;
        platformView.LayoutUpdated -= OnPlatformLayoutUpdated;
        platformView.PreviewKeyDown -= OnPreviewKeyDown;
        platformView.SelectionChanged -= OnSelectionChanged;
        if (_pointerWheelChangedHandler is not null)
        {
            platformView.RemoveHandler(UIElement.PointerWheelChangedEvent, _pointerWheelChangedHandler);
            _pointerWheelChangedHandler = null;
        }
        DetachScrollViewer();
        base.DisconnectHandler(platformView);
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs args) => RefreshInsertionFormat();

    /// <summary>
    /// RichEdit derives both caret color and height from the insertion character format. Make that
    /// format deterministic: every document kind uses the host theme's text color, while Markdown
    /// uses the size of the active paragraph (including headings) instead of a stale prior run.
    /// </summary>
    private void RefreshInsertionFormat()
    {
        if (!CanAccessDocument() || _updatingInsertionFormat) return;
        var selection = PlatformView.Document.Selection;
        if (selection.StartPosition != selection.EndPosition) return;

        var text = MarkdownRichText.Normalize(VirtualView.Text ?? string.Empty);
        var position = Math.Clamp(selection.StartPosition, 0, text.Length);
        var lookup = position;
        if (lookup > 0 && (lookup == text.Length || text[lookup] == '\n') && text[lookup - 1] != '\n')
        {
            lookup--;
        }
        var paragraph = VirtualView.Kind == EditorDocumentKind.Markdown
            ? VirtualView.GetParagraphKind(lookup) : NativeParagraphKind.Normal;
        var size = HeadingSize(BaseFontSize, paragraph);

        _updatingInsertionFormat = true;
        try
        {
            var format = selection.CharacterFormat;
            var color = ToWindowsColor(VirtualView.EditorTheme.Text);
            if (!format.ForegroundColor.Equals(color)) format.ForegroundColor = color;
            if (Math.Abs(format.Size - size) > .01f) format.Size = size;
            if ((VirtualView.Kind == EditorDocumentKind.Code
                    || paragraph == NativeParagraphKind.CodeBlock)
                && !IsMonospace(format.Name))
            {
                format.Name = "Cascadia Mono";
            }
            if (EditorDiagnostics.IsEnabled)
            {
                EditorDiagnostics.Log($"caret kind={VirtualView.Kind} position={position} paragraph={paragraph}"
                    + $" desiredSize={size:F2} actualSize={format.Size:F2}"
                    + $" color={format.ForegroundColor.R},{format.ForegroundColor.G},{format.ForegroundColor.B}");
            }
        }
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
        }
        finally
        {
            _updatingInsertionFormat = false;
        }
    }

    private void OnPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (!CanAccessDocument()) return;

        var control = IsModifierPressed(global::Windows.System.VirtualKey.Control);
        var shift = IsModifierPressed(global::Windows.System.VirtualKey.Shift);
        var undo = control && args.Key == global::Windows.System.VirtualKey.Z && !shift;
        var redo = control && (args.Key == global::Windows.System.VirtualKey.Y
            || args.Key == global::Windows.System.VirtualKey.Z && shift);
        if (undo || redo)
        {
            RestoreFromHistory(undo);
            args.Handled = true;
            return;
        }

        if (control && args.Key == global::Windows.System.VirtualKey.S)
        {
            VirtualView.RaiseSaveRequested();
            args.Handled = true;
            return;
        }
        if (control && args.Key == global::Windows.System.VirtualKey.F)
        {
            ShowFindDialog();
            args.Handled = true;
            return;
        }
        if (control && shift && args.Key == global::Windows.System.VirtualKey.K)
        {
            DeleteCurrentLine();
            args.Handled = true;
            return;
        }
        // RichEditBox handles Ctrl+B/I/U itself by toggling CharacterFormat directly, which does
        // not raise TextChanged (no characters were inserted or removed). CommitCurrentDocument
        // only runs from OnTextChanged or an explicit force, so that native toggle never reached
        // the Markdown model: the bold/italic/underline run appeared on screen but the persisted
        // document, and therefore the saved file, never changed. Route these through
        // ExecuteFormat instead so every formatting change - toolbar, context menu, or shortcut -
        // takes the one path that forces a commit.
        if (control && !shift && VirtualView.Kind == EditorDocumentKind.Markdown && !VirtualView.IsReadOnly
            && args.Key is global::Windows.System.VirtualKey.B or global::Windows.System.VirtualKey.I
                or global::Windows.System.VirtualKey.U)
        {
            var command = args.Key switch
            {
                global::Windows.System.VirtualKey.B => "bold",
                global::Windows.System.VirtualKey.I => "italic",
                _ => "underline",
            };
            ExecuteFormat(command, null);
            args.Handled = true;
            return;
        }
        if (args.Key == global::Windows.System.VirtualKey.Tab
            || control && args.Key is OpenBracketKey or CloseBracketKey)
        {
            var indent = args.Key != OpenBracketKey && !shift;
            IndentSelectedLines(indent);
            args.Handled = true;
            return;
        }
        if (!control && !shift && args.Key == global::Windows.System.VirtualKey.Enter
            && HandleMarkdownReturn())
        {
            args.Handled = true;
        }
    }

    private static bool IsModifierPressed(global::Windows.System.VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & global::Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private void RestoreFromHistory(bool undo)
    {
        if (!CanAccessDocument() || VirtualView.IsReadOnly) return;
        string content;
        var restored = undo
            ? VirtualView.TryUndo(out content)
            : VirtualView.TryRedo(out content);
        if (!restored) return;
        VirtualView.RestoreHistoryContent(content);
        SynchronizeText();
        ApplyPresentation();
    }

    /// <summary>
    /// RichEdit normally clones the current paragraph's list and character format into a new
    /// paragraph. Markdown headings must end on Enter, and an empty list item must become a
    /// normal paragraph on the second Enter, so those two transitions are explicit.
    /// </summary>
    private bool HandleMarkdownReturn()
    {
        if (VirtualView.IsReadOnly || VirtualView.Kind != EditorDocumentKind.Markdown) return false;

        var text = ReadText();
        var selection = PlatformView.Document.Selection;
        var caret = Math.Clamp(Math.Min(selection.StartPosition, selection.EndPosition), 0, text.Length);
        var lines = MarkdownRichText.GetLines(text);
        var line = lines.First(item => caret >= item.Start && caret <= item.Start + item.Length);
        var kind = GetParagraphKind(line, text.Length);

        if (kind is NativeParagraphKind.Bullet or NativeParagraphKind.Ordered && line.Length == 0)
        {
            _applying = true;
            try
            {
                ResetParagraph(PlatformView.Document.GetRange(line.Start, line.Start), NativeParagraphKind.Normal);
                selection.SetRange(line.Start, line.Start);
            }
            finally
            {
                _applying = false;
            }
            CommitCurrentDocument(force: true);
            return true;
        }

        if (kind is not (NativeParagraphKind.Heading1 or NativeParagraphKind.Heading2 or NativeParagraphKind.Heading3))
        {
            return false;
        }

        var insertionStart = caret;
        _applying = true;
        try
        {
            selection.Text = "\r";
            var nextParagraphStart = insertionStart + 1;
            ResetParagraph(PlatformView.Document.GetRange(nextParagraphStart, nextParagraphStart), NativeParagraphKind.Normal);
            selection.SetRange(nextParagraphStart, nextParagraphStart);
        }
        finally
        {
            _applying = false;
        }
        CommitCurrentDocument(force: true);
        return true;
    }

    // Delegates to the model instead of sampling a padded native range. Padding an empty line's
    // range to reach a neighboring character (the old behavior) is exactly what let a native
    // ListType/size query answer with the *next* paragraph's formatting; the model's own spans
    // are the current source of truth for paragraph kind once a document is loaded, and every
    // producer of those spans now agrees on the same (unpadded, line-content-inclusive) bounds.
    private NativeParagraphKind GetParagraphKind((int Start, int Length, bool HasNewLine) line, int textLength) =>
        VirtualView.GetParagraphKind(line.Start);

    private void IndentSelectedLines(bool indent)
    {
        if (VirtualView.IsReadOnly) return;

        var text = ReadText();
        var selection = PlatformView.Document.Selection;
        var start = Math.Clamp(Math.Min(selection.StartPosition, selection.EndPosition), 0, text.Length);
        var end = Math.Clamp(Math.Max(selection.StartPosition, selection.EndPosition), 0, text.Length);
        var lineEnd = end > start && end > 0 && text[end - 1] == '\n' ? end - 1 : end;
        var lines = MarkdownRichText.GetLines(text)
            .Where(line => line.Start <= lineEnd && line.Start + line.Length >= start)
            .ToArray();
        var changes = new List<(int Position, int Delta)>();

        _applying = true;
        try
        {
            foreach (var line in lines.Reverse())
            {
                if (indent)
                {
                    PlatformView.Document.GetRange(line.Start, line.Start).Text = "    ";
                    changes.Add((line.Start, 4));
                    continue;
                }

                var removable = 0;
                while (removable < Math.Min(4, line.Length) && text[line.Start + removable] == ' ') removable++;
                if (removable == 0 && line.Length > 0 && text[line.Start] == '\t') removable = 1;
                if (removable == 0) continue;
                PlatformView.Document.GetRange(line.Start, line.Start + removable).Text = string.Empty;
                changes.Add((line.Start, -removable));
            }

            var adjustedStart = AdjustPosition(start, changes);
            var adjustedEnd = AdjustPosition(end, changes);
            selection.SetRange(Math.Clamp(adjustedStart, 0, ReadText().Length),
                Math.Clamp(adjustedEnd, 0, ReadText().Length));
        }
        finally
        {
            _applying = false;
        }
        CommitCurrentDocument(force: true);
    }

    private static int AdjustPosition(int position, IEnumerable<(int Position, int Delta)> changes) =>
        position + changes.Where(change => change.Position <= position).Sum(change => change.Delta);

    private void DeleteCurrentLine()
    {
        if (VirtualView.IsReadOnly) return;

        var text = ReadText();
        var selection = PlatformView.Document.Selection;
        var caret = Math.Clamp(Math.Min(selection.StartPosition, selection.EndPosition), 0, text.Length);
        var line = MarkdownRichText.GetLines(text)
            .First(item => caret >= item.Start && caret <= item.Start + item.Length);
        var start = line.Start;
        var end = line.Start + line.Length + (line.HasNewLine ? 1 : 0);
        if (!line.HasNewLine && start > 0) start--;

        _applying = true;
        try
        {
            PlatformView.Document.GetRange(start, end).Text = string.Empty;
            selection.SetRange(start, start);
        }
        finally
        {
            _applying = false;
        }
        CommitCurrentDocument(force: true);
    }

    private async void ShowFindDialog()
    {
        if (_findDialog is not null || PlatformView.XamlRoot is null) return;

        var queryBox = new TextBox { PlaceholderText = "Find", Width = 340 };
        var dialog = new ContentDialog
        {
            XamlRoot = PlatformView.XamlRoot,
            Title = "Find in active document",
            Content = queryBox,
            PrimaryButtonText = "Find next",
            CloseButtonText = "Close",
        };
        _findDialog = dialog;
        queryBox.TextChanged += (_, _) => FindText(queryBox.Text, advance: false);
        queryBox.Loaded += (_, _) => queryBox.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            FindText(queryBox.Text, advance: true);
            args.Cancel = true;
        };
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            _findDialog = null;
            PlatformView.Focus(FocusState.Programmatic);
        }
    }

    private void FindText(string? query, bool advance)
    {
        if (string.IsNullOrWhiteSpace(query) || !CanAccessDocument()) return;

        var text = ReadText();
        var selection = PlatformView.Document.Selection;
        var start = Math.Clamp(advance ? selection.EndPosition : selection.StartPosition, 0, text.Length);
        var match = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (match < 0 && start > 0) match = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (match >= 0) selection.SetRange(match, match + query.Length);
    }
    public void ExecuteFormat(string command, string? value)
    {
        if (!CanAccessDocument() || VirtualView.IsReadOnly) return;
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
                case "heading1": ApplyParagraphFormat(selection, NativeParagraphKind.Heading1); break;
                case "heading2": ApplyParagraphFormat(selection, NativeParagraphKind.Heading2); break;
                case "heading3": ApplyParagraphFormat(selection, NativeParagraphKind.Heading3); break;
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
        CommitCurrentDocument(force: true);
    }

    // WinUI's Expand(TextRangeUnit.Paragraph) grows the range to include the trailing paragraph
    // mark, so range.EndPosition lands on the *next* paragraph's start, not this paragraph's own
    // last content position. NativeParagraphSpan uses the latter (Start+Length is this
    // paragraph's own end, inclusive of its final caret gap but excluding the mark) everywhere
    // else - CaptureParagraphs, the parser, the serializer, and both lookup helpers. Passing the
    // raw Expand() span straight into SetParagraphKind planted a span that always overran by one
    // gap position, which is exactly the next paragraph's Start: every heading/list/align toggle
    // silently claimed one position of whatever line followed it, and that claim then persisted
    // (and could keep growing) across every later commit and reformat pass. Trim the mark back
    // off before recording the span.
    private static int ParagraphContentLength(ITextRange range) => Math.Max(0, range.EndPosition - range.StartPosition - 1);

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
        range.CharacterFormat.Size = HeadingSize(BaseFontSize, kind);
        range.CharacterFormat.Bold = kind is NativeParagraphKind.Heading1
            or NativeParagraphKind.Heading2 or NativeParagraphKind.Heading3 ? FormatEffect.On : FormatEffect.Off;
        VirtualView.SetParagraphKind(range.StartPosition, ParagraphContentLength(range), kind);
    }

    private void ApplyAlignment(ITextRange selection)
    {
        var range = selection.GetClone();
        range.Expand(TextRangeUnit.Paragraph);
        range.ParagraphFormat.Alignment = range.ParagraphFormat.Alignment == ParagraphAlignment.Center
            ? ParagraphAlignment.Left : ParagraphAlignment.Center;
        VirtualView.SetParagraphKind(range.StartPosition, ParagraphContentLength(range),
            VirtualView.GetParagraphKind(range.StartPosition), range.ParagraphFormat.Alignment == ParagraphAlignment.Center ? 1 : 0);
    }
    private void ClearFormat(ITextRange selection) => ResetParagraph(selection, NativeParagraphKind.Normal);

    private void ResetParagraph(ITextRange selection, NativeParagraphKind kind)
    {
        var range = selection.GetClone();
        range.Expand(TextRangeUnit.Paragraph);
        range.CharacterFormat.Bold = FormatEffect.Off;
        range.CharacterFormat.Italic = FormatEffect.Off;
        range.CharacterFormat.Underline = UnderlineType.None;
        range.CharacterFormat.Name = "Segoe UI";
        range.CharacterFormat.Size = HeadingSize(BaseFontSize, kind);
        range.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Text);
        range.ParagraphFormat.ListType = kind switch
        {
            NativeParagraphKind.Bullet => MarkerType.Bullet,
            NativeParagraphKind.Ordered => MarkerType.Arabic,
            _ => MarkerType.None,
        };
        range.ParagraphFormat.Alignment = ParagraphAlignment.Left;
        VirtualView.SetParagraphKind(range.StartPosition, ParagraphContentLength(range), kind);
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

    private void CommitCurrentDocument(bool force = false)
    {
        if (!CanAccessDocument()) return;

        var text = ReadText();
        if (!force && string.Equals(text, MarkdownRichText.Normalize(VirtualView.Text ?? string.Empty), StringComparison.Ordinal))
        {
            return;
        }

        var styles = VirtualView.Kind == EditorDocumentKind.Markdown ? CaptureStyles(text) : [];
        var priorText = MarkdownRichText.Normalize(VirtualView.Text ?? string.Empty);
        var fallbackParagraphs = VirtualView.Kind == EditorDocumentKind.Markdown
            ? MarkdownRichText.AdjustParagraphSpans(VirtualView.ParagraphSpans, priorText, text)
            : [];
        var paragraphs = VirtualView.Kind == EditorDocumentKind.Markdown ? CaptureParagraphs(text, fallbackParagraphs) : [];
        _propagatingNativeTextChange = true;
        try
        {
            VirtualView.ReceiveNativeChange(text, styles, paragraphs);
        }
        finally
        {
            _propagatingNativeTextChange = false;
        }
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

    private IReadOnlyList<NativeParagraphSpan> CaptureParagraphs(string text,
        IReadOnlyList<NativeParagraphSpan> fallbackParagraphs)
    {
        var result = new List<NativeParagraphSpan>();
        foreach (var line in MarkdownRichText.GetLines(text))
        {
            var fallback = fallbackParagraphs.FirstOrDefault(item => item.Start <= line.Start
                && item.Start + item.Length >= line.Start);
            NativeParagraphKind kind;
            int alignment;
            if (line.Length == 0)
            {
                // An empty line has no character of its own to sample, and a zero-length native
                // range at this boundary is ambiguous - RichEdit can resolve it to either the
                // paragraph ending here or the one starting here. Widening the range by one
                // position (the old behavior) resolved it to the *next* paragraph's mark, so a
                // blank line typed after a heading or list item silently inherited that
                // formatting instead of being a normal line. Trust the tracked model state
                // (remapped for this edit) for empty lines instead of any native sample.
                kind = fallback?.Kind ?? NativeParagraphKind.Normal;
                alignment = fallback?.Alignment ?? 0;
            }
            else
            {
                var range = PlatformView.Document.GetRange(line.Start, line.Start + 1);
                kind = range.ParagraphFormat.ListType switch
                {
                    MarkerType.Bullet => NativeParagraphKind.Bullet,
                    MarkerType.Arabic => NativeParagraphKind.Ordered,
                    _ when range.CharacterFormat.Size >= BaseFontSize * 1.65f => NativeParagraphKind.Heading1,
                    _ when range.CharacterFormat.Size >= BaseFontSize * 1.4f => NativeParagraphKind.Heading2,
                    _ when range.CharacterFormat.Size >= BaseFontSize * 1.15f => NativeParagraphKind.Heading3,
                    _ => fallback?.Kind ?? NativeParagraphKind.Normal,
                };
                alignment = range.ParagraphFormat.Alignment == ParagraphAlignment.Center ? 1 : 0;
            }
            result.Add(new NativeParagraphSpan(line.Start, line.Length, kind, alignment));
        }
        return result;
    }
    private bool CanAccessDocument() => _isLoaded && !_applying
        && !string.IsNullOrEmpty(VirtualView.DocumentId);

    /// <summary>
    /// WinUI character formatting is expressed in points while MAUI FontSize is in
    /// device-independent pixels, so the raw value rendered noticeably larger than requested.
    /// </summary>
    private float BaseFontSize => (float)Math.Round(
        (VirtualView.FontSize > 0 ? VirtualView.FontSize : 13) * .75, 2);

    private static float HeadingSize(float baseSize, NativeParagraphKind kind) => kind switch
    {
        NativeParagraphKind.Heading1 => baseSize * 1.8f,
        NativeParagraphKind.Heading2 => baseSize * 1.5f,
        NativeParagraphKind.Heading3 => baseSize * 1.25f,
        _ => baseSize,
    };

    private void ApplyAll()
    {
        MapReadOnly(this, VirtualView);
        MapMode(this, VirtualView);
        MapTheme(this, VirtualView);
        SynchronizeText();
        ApplyPresentation();
    }

    /// <summary>
    /// RichEdit paints the insertion caret with the text color in effect at the caret, falling
    /// back to the story's default character format when a position carries no explicit color.
    /// Positions past the last styled character - the trailing paragraph mark, empty documents,
    /// blank trailing lines - therefore used the automatic (system black) color, which is what
    /// made the caret invisible in text and Markdown documents on dark surfaces.
    /// </summary>
    private void ApplyDefaultCharacterFormat()
    {
        try
        {
            var format = PlatformView.Document.GetDefaultCharacterFormat();
            format.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Text);
            format.Name = VirtualView.Kind == EditorDocumentKind.Code ? "Cascadia Mono" : "Segoe UI";
            format.Size = BaseFontSize;
            PlatformView.Document.SetDefaultCharacterFormat(format);
        }
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
            // A surface WinUI still considers unbound retries on the next presentation pass.
        }
    }

    private void ApplyPresentation()
    {
        if (!CanAccessDocument() || PlatformView is null) return;
        var watch = EditorDiagnostics.Start();
        if (VirtualView.Kind == EditorDocumentKind.Code)
        {
            // SynchronizeText establishes the plain document's default font before insertion. Do
            // not query or reformat the native story here; that was another ~70ms UI-thread pass.
            var code = MarkdownRichText.Normalize(VirtualView.Text ?? string.Empty);
            if (code.Length > 0) ScheduleCodePresentation(code);
            else ApplyDefaultCharacterFormat();
            _lineHeight = 0;
            EditorDiagnostics.Stop(watch, $"QueueCodePresentation {code.Length} chars");
            RefreshInsertionFormat();
            VirtualView.RaisePresentationApplied();
            return;
        }

        // Applied before the empty-document guard so an empty editor still shows a themed caret.
        ApplyDefaultCharacterFormat();
        var text = ReadText();
        // RichEditBox owns a final paragraph marker internally. Formatting a zero-length range
        // before layout is the WinUI operation that caused the stowed startup exception.
        if (text.Length == 0)
        {
            EditorDiagnostics.Stop(watch, $"ApplyPresentation kind={VirtualView.Kind} empty");
            return;
        }
        var selection = PlatformView.Document.Selection;
        var start = selection.StartPosition;
        var end = selection.EndPosition;
        _applying = true;
        try
        {
            var baseSize = BaseFontSize;
            // text.Length + 1 reaches the story's final paragraph mark, so a caret parked at the
            // very end of the document inherits the themed color instead of the automatic one.
            var all = PlatformView.Document.GetRange(0, text.Length + 1);
            all.CharacterFormat.ForegroundColor = ToWindowsColor(VirtualView.EditorTheme.Text);
            all.CharacterFormat.Name = VirtualView.Kind == EditorDocumentKind.Code ? "Cascadia Mono" : "Segoe UI";
            all.CharacterFormat.Size = baseSize;
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
                    range.CharacterFormat.Size = HeadingSize(baseSize, paragraph.Kind);
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
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
            // Formatting a surface WinUI considers unbound is not fatal; skip this pass.
        }
        finally
        {
            _applying = false;
        }
        // The base font was just re-applied, so the line step is re-measured during the gutter
        // refresh that RaisePresentationApplied triggers.
        _lineHeight = 0;
        RefreshInsertionFormat();
        EditorDiagnostics.Stop(watch, $"ApplyPresentation kind={VirtualView.Kind} {text.Length} chars");
        VirtualView.RaisePresentationApplied();
    }

    /// <summary>
    /// Builds code colors away from the UI thread, then installs them with one RichEdit RTF write.
    /// Per-range ForegroundColor writes are intentionally forbidden here: WinUI can dereference a
    /// stale native text range after a large document revision and terminate with 0xC0000005.
    /// </summary>
    private void ScheduleCodePresentation(string source)
    {
        CancelCodePresentation();
        if (!_isLoaded || VirtualView.Kind != EditorDocumentKind.Code
            || string.IsNullOrEmpty(VirtualView.DocumentId))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _codePresentationCancellation = cancellation;
        var revision = ++_codePresentationRevision;
        var documentId = VirtualView.DocumentId;
        var spans = VirtualView.TextSpans.ToArray();
        var theme = VirtualView.EditorTheme;
        var pointSize = BaseFontSize;
        _ = BuildAndApplyCodePresentationAsync(source, spans, theme, pointSize,
            documentId, revision, cancellation.Token);
    }

    private async Task BuildAndApplyCodePresentationAsync(string source, NativeTextSpan[] spans,
        EditorTheme theme, float pointSize, string documentId, int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            // Source analysis already yields once for large documents. Build immediately here so
            // the atomic native update is far less likely to collide with the first user click.
            var watch = EditorDiagnostics.Start();
            var rtf = await Task.Run(
                () => CodeRtfFormatter.Build(source, spans, theme, pointSize),
                cancellationToken).ConfigureAwait(false);
            EditorDiagnostics.Stop(watch, $"Build code RTF {source.Length} chars");
            cancellationToken.ThrowIfCancellationRequested();
            PlatformView.DispatcherQueue.TryEnqueue(() =>
                ApplyCodePresentation(rtf, source, documentId, revision));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyCodePresentation(string rtf, string source, string documentId, int revision)
    {
        if (!_isLoaded || !CanAccessDocument() || VirtualView.Kind != EditorDocumentKind.Code
            || revision != _codePresentationRevision || VirtualView.DocumentId != documentId
            || !string.Equals(MarkdownRichText.Normalize(VirtualView.Text ?? string.Empty), source,
                StringComparison.Ordinal))
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var start = selection.StartPosition;
        var end = selection.EndPosition;
        var horizontalOffset = _scrollViewer?.HorizontalOffset;
        var verticalOffset = _scrollViewer?.VerticalOffset;
        var watch = EditorDiagnostics.Start();
        _applying = true;
        try
        {
            PlatformView.Document.SetText(TextSetOptions.FormatRtf, rtf);
            selection.SetRange(Math.Min(start, source.Length), Math.Min(end, source.Length));
            if (EditorDiagnostics.IsEnabled)
            {
                var actual = ReadText();
                EditorDiagnostics.Log($"RTF roundtrip expected={source.Length} actual={actual.Length} equal={string.Equals(source, actual, StringComparison.Ordinal)}");
            }
            if (_scrollViewer is not null)
            {
                _scrollViewer.ChangeView(horizontalOffset, verticalOffset, null, true);
            }
        }
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
            // A superseded or detached native document simply skips this presentation.
        }
        finally
        {
            _applying = false;
        }
        _lineHeight = 0;
        RefreshInsertionFormat();
        EditorDiagnostics.Stop(watch, $"Apply code RTF {source.Length} chars");
        VirtualView.RaisePresentationApplied();
    }

    private void CancelCodePresentation()
    {
        _codePresentationRevision++;
        var cancellation = Interlocked.Exchange(ref _codePresentationCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <summary>Maps a viewport Y coordinate to the character position rendered there.</summary>
    private int StartPositionAt(double y)
    {
        try
        {
            var range = PlatformView.Document.GetRangeFromPoint(
                new global::Windows.Foundation.Point(1, y), PointOptions.ClientCoordinates);
            return range?.StartPosition ?? 0;
        }
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
            return 0;
        }
    }

    /// <summary>
    /// Returns visible source lines at the positions RichEdit actually laid them out. Native line
    /// tops are document coordinates, so subtracting the ScrollViewer offset maps them exactly to
    /// the gutter without accumulating a fractional line-height estimate across a large file.
    /// </summary>
    internal IReadOnlyList<NativeLineMetric> GetVisibleLineMetrics(IReadOnlyList<int> lineStarts)
    {
        if (!_isLoaded || PlatformView is null || lineStarts.Count == 0) return [];
        var viewportHeight = _scrollViewer?.ViewportHeight ?? PlatformView.ActualHeight;
        if (viewportHeight <= 0) return [];

        var contentTop = PlatformView.Padding.Top;
        var offset = _scrollViewer?.VerticalOffset ?? 0;
        var firstLine = FindLineAtOrBeforeTop(lineStarts, offset);
        var fallbackHeight = _lineHeight > 0 ? _lineHeight : ProbeLineStep(lineStarts);
        var metrics = new List<NativeLineMetric>();
        var nativeTop = NativeTopOf(lineStarts, firstLine);

        for (var line = firstLine; line < lineStarts.Count; line++)
        {
            if (double.IsNaN(nativeTop)) break;
            var nextTop = line + 1 < lineStarts.Count ? NativeTopOf(lineStarts, line + 1) : double.NaN;
            var height = !double.IsNaN(nextTop) && nextTop > nativeTop
                ? nextTop - nativeTop : fallbackHeight;
            if (height <= 0) break;
            _lineHeight = height;

            var top = contentTop + nativeTop - offset;
            if (top + height >= 0)
            {
                if (top > contentTop + viewportHeight) break;
                metrics.Add(new NativeLineMetric(line, top, height));
            }
            nativeTop = nextTop;
        }
        return metrics;
    }

    private int FindLineAtOrBeforeTop(IReadOnlyList<int> lineStarts, double documentTop)
    {
        var low = 0;
        var high = lineStarts.Count - 1;
        var result = 0;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var top = NativeTopOf(lineStarts, middle);
            if (double.IsNaN(top)) break;
            if (top <= documentTop)
            {
                result = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return result;
    }

    private double ProbeLineStep(IReadOnlyList<int> lineStarts)
    {
        try
        {
            var first = PlatformView.Document.GetRange(lineStarts[0], lineStarts[0] + 1);
            first.GetRect(PointOptions.ClientCoordinates, out var firstRect, out _);
            if (lineStarts.Count > 1)
            {
                var second = PlatformView.Document.GetRange(lineStarts[1], lineStarts[1] + 1);
                second.GetRect(PointOptions.ClientCoordinates, out var secondRect, out _);
                var step = secondRect.Top - firstRect.Top;
                if (step > 0) return step;
            }
            return firstRect.Height;
        }
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
            return 0;
        }
    }

    /// <summary>Client-coordinate top RichEdit reports for a line, or NaN when unavailable.</summary>
    private double NativeTopOf(IReadOnlyList<int> lineStarts, int line)
    {
        try
        {
            var range = PlatformView.Document.GetRange(lineStarts[line], lineStarts[line] + 1);
            range.GetRect(PointOptions.ClientCoordinates, out var rect, out _);
            return rect.Height > 0 ? rect.Top : double.NaN;
        }
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
            return double.NaN;
        }
    }

    private double DeriveLineStepFromExtent(int lineCount)
    {
        if (_scrollViewer is null || lineCount < 1
            || _scrollViewer.ExtentHeight <= _scrollViewer.ViewportHeight + 1)
        {
            return 0;
        }
        var content = _scrollViewer.ExtentHeight - PlatformView.Padding.Top - PlatformView.Padding.Bottom;
        return content > 0 ? content / lineCount : 0;
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
        // Native character and paragraph formatting is presentation. Disabling RichEditBox's
        // internal undo queue prevents those formatting writes from becoming Ctrl+Z entries;
        // NativeDocumentEditor maintains content-only undo/redo history instead.
        PlatformView.Document.UndoLimit = 0;
        TryAttachScrollViewer();
        ApplyAll();
    }

    private void OnPlatformLayoutUpdated(object? sender, object args)
    {
        // RichEditBox materializes its template after Loaded on some WinUI builds. Keep probing
        // only until the inner ScrollViewer exists so the virtual gutter follows every scroll.
        TryAttachScrollViewer();
    }

    private void TryAttachScrollViewer()
    {
        if (!_isLoaded || PlatformView is null || _scrollViewer is not null) return;
        var scrollViewer = FindDescendant<ScrollViewer>(PlatformView);
        if (scrollViewer is null) return;
        _scrollViewer = scrollViewer;
        _scrollViewer.ViewChanged += OnScrollViewChanged;
        PlatformView.LayoutUpdated -= OnPlatformLayoutUpdated;
        VirtualView.RaiseVerticalOffsetChanged(_scrollViewer.VerticalOffset);
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) =>
        QueueScrollRefresh();

    private void OnPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args) =>
        QueueScrollRefresh();

    private void QueueScrollRefresh()
    {
        if (_scrollRefreshQueued || !_isLoaded || PlatformView is null) return;
        _scrollRefreshQueued = true;
        if (PlatformView.DispatcherQueue.TryEnqueue(() =>
        {
            _scrollRefreshQueued = false;
            if (!_isLoaded || PlatformView is null || VirtualView is null) return;
            TryAttachScrollViewer();
            VirtualView.RaiseVerticalOffsetChanged(_scrollViewer?.VerticalOffset ?? 0);
        }))
        {
            return;
        }
        _scrollRefreshQueued = false;
        VirtualView.RaiseVerticalOffsetChanged(_scrollViewer?.VerticalOffset ?? 0);
    }

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
        if (!CanAccessDocument())
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
            // Establish the base code font/color before inserting the plain document so opening
            // a large file does not require a second full-document CharacterFormat pass.
            ApplyDefaultCharacterFormat();
            PlatformView.Document.SetText(TextSetOptions.None, desired);
            selection.SetRange(Math.Min(start, desired.Length), Math.Min(end, desired.Length));
        }
        catch (Exception exception) when (IsNativeDocumentRejection(exception))
        {
            // WinUI rejects document writes for surfaces it considers unbound. Losing this
            // sync is recoverable; letting the native exception escape terminates the process.
        }
        finally
        {
            _applying = false;
        }
    }

    private static bool IsNativeDocumentRejection(Exception exception) =>
        exception is UnauthorizedAccessException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or ArgumentException;

    private static void MapText(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        if (!handler.CanAccessDocument())
        {
            return;
        }

        // A code change is already followed by PresentationRevision from CodeEditorView.
        // Skipping this mapper pass avoids a duplicate full RichEditBox formatting cycle.
        if (handler._propagatingNativeTextChange && view.Kind == EditorDocumentKind.Code)
        {
            return;
        }

        handler.SynchronizeText();
        handler.ApplyPresentation();
    }

    private static void MapReadOnly(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        handler.PlatformView.IsReadOnly = view.IsReadOnly;
        // Cut, paste and formatting are offered only where the document can change.
        handler.BuildContextFlyout();
    }

    private static void MapEnabled(NativeDocumentEditorHandler handler, NativeDocumentEditor view) =>
        handler.PlatformView.IsEnabled = view.IsEnabled;

    private static void MapMode(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        // The document font changes with the mode, so the cached line step is no longer valid.
        handler._lineHeight = 0;
        handler.PlatformView.IsSpellCheckEnabled = view.Kind == EditorDocumentKind.Markdown;
        handler.PlatformView.TextWrapping = view.Kind == EditorDocumentKind.Code ? TextWrapping.NoWrap : TextWrapping.Wrap;
        handler.BuildContextFlyout();
    }

    private static void MapTheme(NativeDocumentEditorHandler handler, NativeDocumentEditor view)
    {
        // The document font may change with the palette, so the cached line step is re-measured.
        handler._lineHeight = 0;
        // Mutating the shared brushes keeps the control's brush *instances* stable, so the
        // template's state changes never reassign Foreground and never clear character formats.
        handler._backgroundBrush.Color = ToWindowsColor(view.EditorTheme.Surface);
        handler._foregroundBrush.Color = ToWindowsColor(view.EditorTheme.Text);
        handler._selectionBrush.Color = ToWindowsColor(view.EditorTheme.Accent);
        handler._borderBrush.Color = TransparentColor;
        // The insertion caret is drawn by RichEdit itself in the element theme's text color, not
        // from any brush we can set. A light host surface under the system's dark theme therefore
        // produced a white caret that only showed where it crossed a glyph. Aligning the element
        // theme with the surface makes the caret, and every other built-in visual, follow it.
        handler.PlatformView.RequestedTheme = IsDarkSurface(view.EditorTheme.Surface)
            ? ElementTheme.Dark : ElementTheme.Light;
    }

    private static bool IsDarkSurface(Color color) =>
        (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue) < 0.5;

    private static void MapPresentation(NativeDocumentEditorHandler handler, NativeDocumentEditor view) =>
        handler.QueuePresentation();

    /// <summary>
    /// Collapses repeated presentation requests into one pass per UI tick. Loading a document
    /// raises several revisions in a row (document load, syntax spans, fold spans, theme), and
    /// each full pass reformats the whole document.
    /// </summary>
    private void QueuePresentation()
    {
        if (_presentationQueued || !_isLoaded || string.IsNullOrEmpty(VirtualView?.DocumentId))
        {
            return;
        }

        _presentationQueued = true;
        if (PlatformView.DispatcherQueue.TryEnqueue(() =>
        {
            _presentationQueued = false;
            // The handler may have been disconnected or the document cleared between the
            // request and this callback.
            if (_isLoaded && PlatformView is not null && VirtualView is not null)
            {
                ApplyPresentation();
            }
        }))
        {
            return;
        }

        _presentationQueued = false;
        ApplyPresentation();
    }

    private static FormatEffect Toggle(FormatEffect value) => value == FormatEffect.On ? FormatEffect.Off : FormatEffect.On;
    private static bool IsMonospace(string? name) => name?.Contains("Mono", StringComparison.OrdinalIgnoreCase) == true
        || name?.Contains("Consolas", StringComparison.OrdinalIgnoreCase) == true;

    private static global::Windows.UI.Color ToWindowsColor(Color color) => global::Windows.UI.Color.FromArgb(
        (byte)Math.Round(color.Alpha * 255), (byte)Math.Round(color.Red * 255),
        (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255));
}