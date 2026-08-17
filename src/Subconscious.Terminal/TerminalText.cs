using System.Globalization;
using System.Text;

namespace Subconscious.Terminal;

internal static class TerminalText
{
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        width = Math.Max(1, width);
        var lines = new List<string>();
        var line = new StringBuilder();
        var cells = 0;

        foreach (var element in TextElements(text))
        {
            if (element == "\r") continue;
            if (element == "\n")
            {
                lines.Add(line.ToString());
                line.Clear();
                cells = 0;
                continue;
            }

            var elementWidth = CellWidth(element);
            if (cells > 0 && cells + elementWidth > width)
            {
                lines.Add(line.ToString());
                line.Clear();
                cells = 0;
            }
            line.Append(element);
            cells += elementWidth;
        }

        lines.Add(line.ToString());
        return lines;
    }

    public static (int Row, int Column) Position(string text, int utf16Index, int width)
    {
        width = Math.Max(1, width);
        var row = 0;
        var column = 0;
        var consumed = 0;
        foreach (var element in TextElements(text))
        {
            if (consumed >= utf16Index) break;
            consumed += element.Length;
            if (element == "\r") continue;
            if (element == "\n") { row++; column = 0; continue; }
            var cells = CellWidth(element);
            if (column > 0 && column + cells > width) { row++; column = 0; }
            column += cells;
        }
        return (row, column);
    }

    public static int PreviousElement(string text, int index)
    {
        if (index <= 0) return 0;
        return StringInfo.ParseCombiningCharacters(text[..index]).LastOrDefault();
    }

    public static int NextElement(string text, int index)
    {
        if (index >= text.Length) return text.Length;
        var starts = StringInfo.ParseCombiningCharacters(text[index..]);
        return starts.Length > 1 ? index + starts[1] : text.Length;
    }

    public static int CellWidth(string element)
    {
        if (element.Length == 0) return 0;
        if (element.Contains('\u200d') || element.Contains('\ufe0f')) return 2;
        Rune.DecodeFromUtf16(element, out var rune, out _);
        var value = rune.Value;
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        if (value < 32 || value is >= 0x7f and < 0xa0) return 0;
        return IsWide(value) ? 2 : 1;
    }

    private static IEnumerable<string> TextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }

    private static bool IsWide(int value) =>
        value is >= 0x1100 and <= 0x115f
        or >= 0x2329 and <= 0x232a
        or >= 0x2e80 and <= 0xa4cf
        or >= 0xac00 and <= 0xd7a3
        or >= 0xf900 and <= 0xfaff
        or >= 0xfe10 and <= 0xfe19
        or >= 0xfe30 and <= 0xfe6f
        or >= 0xff00 and <= 0xff60
        or >= 0xffe0 and <= 0xffe6
        or >= 0x1f300 and <= 0x1faff
        or >= 0x20000 and <= 0x3fffd;
}
