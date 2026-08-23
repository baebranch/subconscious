using System.Text;
using Subconscious.TUI;

namespace Subconscious.Terminal;

internal sealed class TerminalRenderer : Widget
{
    private readonly TerminalSession _terminal;
    private readonly object _sync = new();
    private readonly List<TranscriptBlock> _blocks = [];
    private TerminalTheme _theme = TerminalTheme.Default;
    private TerminalView _view = new(
        "Starting…", string.Empty, string.Empty, 0, false, null, null,
        new SidebarView(true, false, SidebarMode.Workspaces, 0, []));
    private int _scrollFromBottom;
    private int _transcriptPageSize = 1;
    private bool _plainStreamOpen;

    public TerminalRenderer(TerminalSession terminal) => _terminal = terminal;

    private TerminalPalette Palette => _theme.Palette;

    public void SetTheme(TerminalTheme theme)
    {
        lock (_sync) _theme = theme;
    }

    public void UpdateView(TerminalView view)
    {
        lock (_sync) _view = view;
    }

    public void CommitLogo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.txt");
        var logo = File.Exists(path) ? File.ReadAllText(path).TrimEnd() : "SUBCONSCIOUS";
        Commit(new TranscriptBlock(BlockKind.Logo, "", logo));
        if (!_terminal.Interactive)
        {
            Console.WriteLine(logo);
            Console.WriteLine("A native terminal client for the Subconscious engine");
            Console.WriteLine();
        }
    }

    public void CommitSection(string title, string? subtitle = null)
    {
        Commit(new TranscriptBlock(BlockKind.Section, title, subtitle ?? string.Empty));
        if (!_terminal.Interactive)
        {
            Console.WriteLine($"── {title}");
            if (!string.IsNullOrWhiteSpace(subtitle)) Console.WriteLine(subtitle);
        }
    }

    public void CommitMessage(string role, string content)
    {
        content = Sanitize(content).TrimEnd();
        Commit(new TranscriptBlock(BlockKind.Message, role, content));
        if (!_terminal.Interactive)
        {
            Console.WriteLine(role);
            Console.WriteLine(content);
            Console.WriteLine();
        }
    }

    public void CommitNotice(string text, bool error = false)
    {
        text = Sanitize(text);
        Commit(new TranscriptBlock(error ? BlockKind.Error : BlockKind.Notice, "", text));
        if (!_terminal.Interactive) Console.WriteLine($"◆ {text}");
    }

    public void BeginPlainAssistant()
    {
        if (_terminal.Interactive) return;
        Console.Write("assistant ");
        _plainStreamOpen = true;
    }

    public void AppendPlainDelta(string delta)
    {
        if (_terminal.Interactive) return;
        Console.Out.Write(Sanitize(delta));
        Console.Out.Flush();
    }

    public void EndPlainAssistant()
    {
        if (_terminal.Interactive || !_plainStreamOpen) return;
        Console.WriteLine();
        Console.WriteLine();
        _plainStreamOpen = false;
    }

    public void ClearScreen()
    {
        lock (_sync)
        {
            _blocks.Clear();
            _scrollFromBottom = 0;
        }
        if (!_terminal.Interactive)
        {
            try { Console.Clear(); } catch (IOException) { }
        }
    }

    public void ClearLive() { }

    public override bool OnScroll(TerminalScrollEvent scroll)
    {
        lock (_sync)
        {
            var amount = scroll.IsPageScroll ? scroll.Delta * _transcriptPageSize : scroll.Delta;
            var previous = _scrollFromBottom;
            _scrollFromBottom = Math.Max(0, _scrollFromBottom - amount);
            return previous != _scrollFromBottom;
        }
    }

    public override void Render()
    {
        TerminalView view;
        TerminalTheme theme;
        List<TranscriptBlock> blocks;
        lock (_sync)
        {
            view = _view;
            theme = _theme;
            blocks = [.. _blocks];
        }

        if (Bounds.Width < 40 || Bounds.Height < 12)
        {
            TuiTerminal.HideCursor();
            TuiTerminal.MoveTo(Bounds.Left, Bounds.Top);
            TuiTerminal.Write("Resize terminal to at least 40×12.");
            return;
        }

        TuiTerminal.HideCursor();
        var usable = new UiRect(Bounds.Left, Bounds.Top, Math.Max(1, Bounds.Width - 1), Bounds.Height);
        var composerWidth = Math.Max(6, usable.Width - 6);
        var composerLines = TerminalText.Wrap(view.ComposerText, composerWidth);
        var composerHeight = Math.Clamp(composerLines.Count + 2, 3, 6);
        var statusRow = usable.Bottom - composerHeight;
        var bodyTop = usable.Top + 3;
        var bodyHeight = Math.Max(3, statusRow - bodyTop);
        var showSidebar = view.Sidebar.IsVisible && (usable.Width >= 72 || view.Sidebar.IsFocused);
        var sidebarOverlay = showSidebar && usable.Width < 72;
        var sidebarWidth = showSidebar
            ? sidebarOverlay ? usable.Width : Math.Clamp(usable.Width / 3, 25, 36)
            : 0;
        var transcriptWidth = sidebarOverlay ? usable.Width : usable.Width - sidebarWidth;

        RenderHeader(new UiRect(usable.Left, usable.Top, usable.Width, 3), theme.Palette);
        var transcriptArea = new UiRect(usable.Left, bodyTop, transcriptWidth, bodyHeight);
        RenderTranscript(transcriptArea, blocks, view.StreamingText, theme.Palette);
        if (showSidebar)
        {
            var sidebarArea = sidebarOverlay
                ? transcriptArea
                : new UiRect(transcriptArea.Right + 1, bodyTop, sidebarWidth, bodyHeight);
            RenderSidebar(sidebarArea, view.Sidebar, theme.Palette);
        }
        RenderStatus(new UiRect(usable.Left, statusRow, usable.Width, 1), view, theme.Palette);
        RenderComposer(new UiRect(usable.Left, statusRow + 1, usable.Width, composerHeight), view, composerLines, theme.Palette);
        if (view.Selection is not null) RenderSelection(view.Selection, theme.Palette);
        if (view.Approval is not null) RenderApproval(view.Approval, theme.Palette);
        if (view.Sidebar.IsFocused || view.Selection is not null || view.Approval is not null)
        {
            TuiTerminal.HideCursor();
        }
    }

    private void RenderHeader(UiRect area, TerminalPalette palette)
    {
        var panel = new Panel("Subconscious Terminal") { BorderColor = ConsoleColor.DarkCyan };
        panel.Resize(area);
        panel.Render();
        Apply(palette.Muted, dim: true);
        WriteClipped(area.Left + 2, area.Top + 1, "Native TUI client · Ctrl+B sidebar · Ctrl+1/2/3 sections", area.Width - 4);
        TuiTerminal.Reset();
    }

    private void RenderTranscript(UiRect area, IReadOnlyList<TranscriptBlock> blocks, string streaming, TerminalPalette palette)
    {
        var panel = new Panel("Transcript · scrollable") { BorderColor = ConsoleColor.DarkCyan };
        panel.Resize(area);
        panel.Render();
        var width = Math.Max(1, area.Width - 4);
        var lines = BuildTranscriptLines(blocks, width, palette);
        if (!string.IsNullOrEmpty(streaming))
        {
            lines.Add(new StyledLine("assistant", palette.Assistant, Bold: true));
            lines.AddRange(TerminalText.Wrap(Sanitize(streaming), width)
                .Select(line => new StyledLine(line, palette.Stream)));
        }

        var available = Math.Max(1, area.Height - 2);
        int offset;
        lock (_sync)
        {
            _transcriptPageSize = available;
            _scrollFromBottom = Math.Clamp(_scrollFromBottom, 0, Math.Max(0, lines.Count - available));
            offset = _scrollFromBottom;
        }
        var first = Math.Max(0, lines.Count - available - offset);
        foreach (var pair in lines.Skip(first).Take(available).Select((line, index) => (line, index)))
        {
            Apply(pair.line.Color, pair.line.Bold, pair.line.Dim);
            WriteClipped(area.Left + 2, area.Top + 1 + pair.index, pair.line.Text, width);
        }
        TuiTerminal.Reset();
    }

    private static List<StyledLine> BuildTranscriptLines(
        IReadOnlyList<TranscriptBlock> blocks,
        int width,
        TerminalPalette palette)
    {
        var lines = new List<StyledLine>();
        foreach (var block in blocks)
        {
            AddBlockLines(lines, block, width, palette);
        }
        return lines;
    }

    private static void AddBlockLines(List<StyledLine> lines, TranscriptBlock block, int width, TerminalPalette palette)
    {
        switch (block.Kind)
        {
            case BlockKind.Logo:
                lines.AddRange(block.Content.Replace("\r\n", "\n").Split('\n')
                    .Select(line => new StyledLine(line, palette.Accent, Bold: true)));
                lines.Add(new StyledLine("A native terminal client for the Subconscious engine", palette.Muted, Dim: true));
                break;
            case BlockKind.Section:
                lines.Add(new StyledLine($"── {block.Label}", palette.Accent, Bold: true));
                if (block.Content.Length > 0) lines.Add(new StyledLine(block.Content, palette.Muted));
                break;
            case BlockKind.Message:
                var color = block.Label.Equals("user", StringComparison.OrdinalIgnoreCase)
                    ? palette.User
                    : block.Label.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                        ? palette.Assistant : palette.Accent;
                lines.Add(new StyledLine(block.Label, color, Bold: true));
                AddContentLines(lines, block.Content, width, color, palette);
                lines.Add(new StyledLine(string.Empty, palette.Muted));
                break;
            case BlockKind.Error:
                AddWrapped(lines, $"◆ {block.Content}", width, palette.Error);
                break;
            default:
                AddWrapped(lines, $"◆ {block.Content}", width, palette.Muted);
                break;
        }
    }

    private static void AddContentLines(List<StyledLine> lines, string content, int width, ThemeColor color, TerminalPalette palette)
    {
        var inCode = false;
        foreach (var source in content.Replace("\r\n", "\n").Split('\n'))
        {
            if (source.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            var heading = !inCode && source.StartsWith('#');
            var text = heading ? source.TrimStart('#', ' ') : inCode ? $"  {source}" : source;
            AddWrapped(lines, text, width, inCode ? palette.Code : heading ? palette.Accent : color, heading);
        }
    }

    private static void AddWrapped(List<StyledLine> lines, string text, int width, ThemeColor color, bool bold = false)
    {
        lines.AddRange(TerminalText.Wrap(text, width).Select(line => new StyledLine(line, color, bold)));
    }

    private void RenderSidebar(UiRect area, SidebarView sidebar, TerminalPalette palette)
    {
        var panel = new Panel($"Sidebar · {sidebar.Mode}") { BorderColor = sidebar.IsFocused ? ConsoleColor.Cyan : ConsoleColor.DarkCyan };
        panel.Resize(area);
        panel.Render();
        var width = Math.Max(1, area.Width - 4);
        Apply(palette.Muted, dim: true);
        WriteClipped(area.Left + 2, area.Top + 1, "1 Workspaces  2 Threads  3 Settings", width);
        var available = Math.Max(0, area.Height - 3);
        if (sidebar.Items.Count == 0)
        {
            WriteClipped(area.Left + 2, area.Top + 2, "(none)", width);
            TuiTerminal.Reset();
            return;
        }

        var first = Math.Max(0, Math.Min(sidebar.SelectedIndex - (available / 2), sidebar.Items.Count - available));
        foreach (var pair in sidebar.Items.Skip(first).Take(available).Select((item, index) => (item, index: first + index)))
        {
            var selected = sidebar.IsFocused && pair.index == sidebar.SelectedIndex;
            Apply(pair.item.IsActive ? palette.Accent : palette.Muted, bold: pair.item.IsActive, inverse: selected);
            var marker = pair.item.IsActive ? "✓" : selected ? "›" : " ";
            WriteClipped(area.Left + 2, area.Top + 2 + pair.index - first, $"{marker} {pair.item.Label}", width);
        }
        TuiTerminal.Reset();
    }

    private void RenderStatus(UiRect area, TerminalView view, TerminalPalette palette)
    {
        TuiTerminal.MoveTo(area.Left, area.Top);
        TuiTerminal.Write(new string(' ', area.Width));
        Apply(palette.Muted, dim: true);
        WriteClipped(area.Left, area.Top, view.Status + (view.Busy ? " ◐" : string.Empty), area.Width);
        TuiTerminal.Reset();
    }

    private void RenderComposer(UiRect area, TerminalView view, IReadOnlyList<string> lines, TerminalPalette palette)
    {
        var panel = new Panel("Composer · pinned") { BorderColor = ConsoleColor.DarkCyan };
        panel.Resize(area);
        panel.Render();
        var width = Math.Max(1, area.Width - 4);
        foreach (var pair in lines.Take(area.Height - 2).Select((line, index) => (line, index)))
        {
            Apply(palette.Composer, bold: pair.index == 0);
            WriteClipped(area.Left + 2, area.Top + 1 + pair.index, pair.index == 0 ? $"❯ {pair.line}" : $"  {pair.line}", width);
        }
        var position = TerminalText.Position(view.ComposerText, view.ComposerCaret, Math.Max(1, width - 2));
        TuiTerminal.Reset();
        TuiTerminal.MoveTo(Math.Min(area.Right - 1, area.Left + 4 + position.Column), Math.Min(area.Bottom - 1, area.Top + 1 + position.Row));
        TuiTerminal.ShowCursor();
    }

    private void RenderSelection(SelectionOverlay overlay, TerminalPalette palette)
    {
        var width = Math.Min(48, Bounds.Width - 4);
        var height = Math.Min(12, overlay.Items.Count + 3);
        var area = Center(width, Math.Max(4, height));
        var panel = new Panel(overlay.Title) { BorderColor = ConsoleColor.Cyan };
        panel.Resize(area);
        panel.Render();
        if (overlay.Items.Count == 0)
        {
            WriteClipped(area.Left + 2, area.Top + 1, "(none)", area.Width - 4);
            return;
        }
        var available = area.Height - 2;
        var first = Math.Max(0, Math.Min(overlay.SelectedIndex - (available / 2), overlay.Items.Count - available));
        foreach (var pair in overlay.Items.Skip(first).Take(available).Select((item, index) => (item, index: first + index)))
        {
            Apply(palette.Accent, inverse: pair.index == overlay.SelectedIndex);
            WriteClipped(area.Left + 2, area.Top + 1 + pair.index - first, $"{(pair.index == overlay.SelectedIndex ? "›" : " ")} {pair.item.Label}", area.Width - 4);
        }
        TuiTerminal.Reset();
    }

    private void RenderApproval(PendingApproval approval, TerminalPalette palette)
    {
        var area = Center(Math.Min(60, Bounds.Width - 4), 8);
        var panel = new Panel("Tool approval required") { BorderColor = ConsoleColor.Yellow };
        panel.Resize(area);
        panel.Render();
        Apply(palette.Warning, bold: true);
        WriteClipped(area.Left + 2, area.Top + 1, $"{approval.Request.ToolName} · {approval.Request.Operation}", area.Width - 4);
        Apply(palette.Code);
        WriteClipped(area.Left + 2, area.Top + 2, approval.Request.Arguments, area.Width - 4);
        Apply(palette.Warning, inverse: !approval.ApproveSelected);
        WriteClipped(area.Left + 2, area.Top + 4, " deny ", 8);
        Apply(palette.Warning, inverse: approval.ApproveSelected);
        WriteClipped(area.Left + 12, area.Top + 4, " approve ", 10);
        Apply(palette.Muted, dim: true);
        WriteClipped(area.Left + 2, area.Top + 5, "←/→ choose · Enter confirm · y/n", area.Width - 4);
        TuiTerminal.Reset();
    }

    private UiRect Center(int width, int height) => new(
        Bounds.Left + Math.Max(0, (Bounds.Width - width) / 2),
        Bounds.Top + Math.Max(0, (Bounds.Height - height) / 2),
        width,
        height);

    private void Commit(TranscriptBlock block)
    {
        if (!_terminal.Interactive) return;
        lock (_sync)
        {
            _blocks.Add(block);
            _scrollFromBottom = 0;
        }
    }

    private static void Apply(ThemeColor color, bool bold = false, bool dim = false, bool inverse = false)
    {
        TuiTerminal.Reset();
        if (color.Sgr.StartsWith("38;2;", StringComparison.Ordinal))
        {
            var parts = color.Sgr.Split(';');
            TuiTerminal.SetForegroundRgb(byte.Parse(parts[2]), byte.Parse(parts[3]), byte.Parse(parts[4]));
        }
        else if (color.Sgr == "39")
        {
            TuiTerminal.SetDefaultForeground();
        }
        else
        {
            TuiTerminal.SetForeground(color.Sgr switch
            {
                "31" => ConsoleColor.Red,
                "33" => ConsoleColor.Yellow,
                "90" => ConsoleColor.DarkGray,
                _ => ConsoleColor.Gray,
            });
        }
        if (bold) TuiTerminal.SetBold();
        if (dim) TuiTerminal.SetDim();
        if (inverse) TuiTerminal.SetInverse();
    }

    private static void WriteClipped(int column, int row, string text, int width)
    {
        TuiTerminal.MoveTo(column, row);
        TuiTerminal.Write(TrimToWidth(text, width));
    }

    private static string TrimToWidth(string value, int width)
    {
        value = Sanitize(value).Replace('\n', ' ').Replace('\r', ' ');
        var result = new StringBuilder();
        var cells = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var text = rune.ToString();
            var runeWidth = TerminalText.CellWidth(text);
            if (cells + runeWidth > width) break;
            result.Append(text);
            cells += runeWidth;
        }
        return result.ToString();
    }

    private static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\n' or '\t' || character >= ' ') result.Append(character);
        }
        return result.ToString();
    }

    private enum BlockKind
    {
        Logo,
        Section,
        Message,
        Notice,
        Error,
    }

    private sealed record TranscriptBlock(BlockKind Kind, string Label, string Content);
    private sealed record StyledLine(string Text, ThemeColor Color, bool Bold = false, bool Dim = false);
}
