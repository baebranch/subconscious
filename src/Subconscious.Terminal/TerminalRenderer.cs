using System.Text;
using Spectre.Console;

namespace Subconscious.Terminal;

internal sealed class TerminalRenderer
{
    private readonly TerminalSession _terminal;
    private IReadOnlyList<string> _liveLines = [];
    private TerminalTheme _theme = TerminalTheme.Default;
    private int _cursorRow;
    private bool _plainStreamOpen;

    public TerminalRenderer(TerminalSession terminal) => _terminal = terminal;

    private TerminalPalette Palette => _theme.Palette;

    public void SetTheme(TerminalTheme theme)
    {
        ClearLive();
        _theme = theme;
    }

    public void CommitLogo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.txt");
        var logo = File.Exists(path) ? File.ReadAllText(path).TrimEnd() : "SUBCONSCIOUS";
        ClearLive();
        AnsiConsole.MarkupLine($"[{MarkupStyle(Palette.Accent, bold: true)}]{Markup.Escape(Sanitize(logo))}[/]");
        AnsiConsole.MarkupLine($"[{MarkupStyle(Palette.Muted)}]A native terminal client for the Subconscious engine[/]");
        AnsiConsole.WriteLine();
    }

    public void CommitSection(string title, string? subtitle = null)
    {
        ClearLive();
        AnsiConsole.MarkupLine($"[{MarkupStyle(Palette.Accent, bold: true)}]── {Markup.Escape(Sanitize(title))}[/]");
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            AnsiConsole.MarkupLine($"[{MarkupStyle(Palette.Muted)}]{Markup.Escape(Sanitize(subtitle))}[/]");
        }
    }

    public void CommitMessage(string role, string content)
    {
        content = Sanitize(content).TrimEnd();
        ClearLive();
        var color = role.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? Palette.User
            : role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? Palette.Assistant
                : Palette.Accent;
        AnsiConsole.MarkupLine($"[{MarkupStyle(color, bold: true)}]{Markup.Escape(role)}[/]");
        WriteMarkdown(content);
        AnsiConsole.WriteLine();
    }

    public void CommitNotice(string text, bool error = false)
    {
        ClearLive();
        var color = error ? Palette.Error : Palette.Muted;
        AnsiConsole.MarkupLine($"[{MarkupStyle(color)}]◆ {Markup.Escape(Sanitize(text))}[/]");
    }

    public void BeginPlainAssistant()
    {
        if (_terminal.Interactive) return;
        AnsiConsole.Markup($"[{MarkupStyle(Palette.Assistant, bold: true)}]assistant[/] ");
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
        Console.Out.WriteLine();
        Console.Out.WriteLine();
        _plainStreamOpen = false;
    }

    public void Render(TerminalView view)
    {
        if (!_terminal.Interactive) return;
        // Never occupy the last physical column: Windows auto-wraps there, which would move
        // the real cursor without the diff renderer observing it and corrupt picker geometry.
        var width = Math.Max(1, _terminal.Width - 1);
        var lines = new List<string>();

        var fixedRows = 2;
        if (view.Selection is not null) fixedRows += Math.Min(10, view.Selection.Items.Count + 2);
        if (view.Approval is not null) fixedRows += 5;
        var streamBudget = Math.Max(0, _terminal.Height - fixedRows - 2);
        if (view.StreamingText.Length > 0 && streamBudget > 0)
        {
            var wrapped = TerminalText.Wrap(Sanitize(view.StreamingText), width - 2);
            foreach (var line in wrapped.TakeLast(streamBudget))
            {
                lines.Add($"{Palette.Stream.Paint("│")} {line}");
            }
        }

        if (view.Selection is not null) AddSelection(lines, view.Selection, width);
        if (view.Approval is not null) AddApproval(lines, view.Approval, width);

        var activity = view.Busy ? " ◐" : string.Empty;
        lines.Add(Palette.Muted.Paint(TrimToWidth(view.Status + activity, width), dim: true));

        var composerWidth = Math.Max(1, width - 2);
        var composerLines = TerminalText.Wrap(view.ComposerText, composerWidth);
        var composerStart = lines.Count;
        for (var index = 0; index < composerLines.Count; index++)
        {
            var prefix = index == 0 ? $"{Palette.Composer.Paint("❯", bold: true)} " : "  ";
            lines.Add(prefix + composerLines[index]);
        }

        var position = TerminalText.Position(view.ComposerText, view.ComposerCaret, composerWidth);
        var caretRow = Math.Min(lines.Count - 1, composerStart + position.Row);
        var caretColumn = Math.Min(width - 1, position.Column + 2);
        WriteLiveFrame(lines, caretRow, caretColumn);
    }

    public void ClearScreen()
    {
        ClearLive();
        if (_terminal.Interactive)
        {
            Console.Out.Write("\u001b[2J\u001b[H");
            Console.Out.Flush();
        }
    }

    public void ClearLive()
    {
        if (!_terminal.Interactive || _liveLines.Count == 0) return;
        var output = new StringBuilder("\u001b[?25l");
        if (_cursorRow > 0) output.Append($"\u001b[{_cursorRow}A");
        for (var row = 0; row < _liveLines.Count; row++)
        {
            output.Append("\r\u001b[2K");
            if (row < _liveLines.Count - 1) output.Append("\u001b[1B");
        }
        if (_liveLines.Count > 1) output.Append($"\u001b[{_liveLines.Count - 1}A");
        output.Append("\r\u001b[?25h");
        Console.Out.Write(output.ToString());
        Console.Out.Flush();
        _liveLines = [];
        _cursorRow = 0;
    }

    private void WriteLiveFrame(IReadOnlyList<string> next, int caretRow, int caretColumn)
    {
        var output = new StringBuilder("\u001b[?25l");
        if (_liveLines.Count > 0 && _cursorRow > 0) output.Append($"\u001b[{_cursorRow}A");
        var rowCount = Math.Max(_liveLines.Count, next.Count);
        for (var row = 0; row < rowCount; row++)
        {
            var oldLine = row < _liveLines.Count ? _liveLines[row] : null;
            var newLine = row < next.Count ? next[row] : null;
            if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                output.Append("\r\u001b[2K");
                if (newLine is not null) output.Append(newLine);
            }
            if (row < rowCount - 1)
            {
                output.Append(row + 1 < _liveLines.Count ? "\u001b[1B" : "\r\n");
            }
        }

        var currentRow = Math.Max(0, rowCount - 1);
        if (currentRow > caretRow) output.Append($"\u001b[{currentRow - caretRow}A");
        output.Append('\r');
        if (caretColumn > 0) output.Append($"\u001b[{caretColumn}C");
        output.Append("\u001b[?25h");
        Console.Out.Write(output.ToString());
        Console.Out.Flush();
        _liveLines = next.ToArray();
        _cursorRow = caretRow;
    }

    private void AddSelection(List<string> lines, SelectionOverlay overlay, int width)
    {
        lines.Add(Palette.Accent.Paint(TrimToWidth(overlay.Title, width), bold: true));
        if (overlay.Items.Count == 0)
        {
            lines.Add("  (none)");
            return;
        }
        var first = Math.Max(0, Math.Min(overlay.SelectedIndex - 4, overlay.Items.Count - 9));
        foreach (var pair in overlay.Items.Skip(first).Take(9).Select((item, index) => (item, index: first + index)))
        {
            var label = TrimToWidth($"  {pair.item.Label}", width);
            lines.Add(pair.index == overlay.SelectedIndex
                ? $"\u001b[7m› {label.TrimStart()}\u001b[0m"
                : label);
        }
    }

    private void AddApproval(List<string> lines, PendingApproval approval, int width)
    {
        var request = approval.Request;
        lines.Add(Palette.Warning.Paint("Tool approval required", bold: true));
        lines.Add(TrimToWidth($"{request.ToolName} · {request.Operation}", width));
        lines.Add(Palette.Code.Paint(TrimToWidth(request.Arguments, width)));
        lines.Add(approval.ApproveSelected
            ? "  [deny]  \u001b[7m approve \u001b[0m"
            : "  \u001b[7m deny \u001b[0m  [approve]");
        lines.Add(Palette.Muted.Paint("←/→ choose · Enter confirm · y/n", dim: true));
    }

    private void WriteMarkdown(string content)
    {
        var inCode = false;
        foreach (var sourceLine in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = sourceLine;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }
            if (inCode)
            {
                AnsiConsole.MarkupLine($"[{MarkupStyle(Palette.Code)}]  {Markup.Escape(line)}[/]");
            }
            else if (line.StartsWith('#'))
            {
                AnsiConsole.MarkupLine($"[{MarkupStyle(Palette.Accent, bold: true)}]{Markup.Escape(line.TrimStart('#', ' '))}[/]");
            }
            else
            {
                AnsiConsole.WriteLine(line);
            }
        }
    }

    private static string MarkupStyle(ThemeColor color, bool bold = false)
    {
        var weight = bold ? "bold" : string.Empty;
        var foreground = color.Markup == "default" ? string.Empty : color.Markup;
        return string.Join(' ', new[] { weight, foreground }.Where(value => value.Length > 0));
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
}
