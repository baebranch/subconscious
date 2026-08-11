using System.Text;
using System.Text.RegularExpressions;

namespace Subconscious.WYSIWYG;

[Flags]
internal enum NativeTextStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Code = 8,
}

internal enum NativeParagraphKind
{
    Normal,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    Bullet,
    Ordered,
    CodeBlock,
    HorizontalRule,
}

internal sealed record NativeTextSpan(int Start, int Length, NativeTextStyle Style, string? Link = null);
internal sealed record NativeParagraphSpan(int Start, int Length, NativeParagraphKind Kind, int Alignment = 0);
internal sealed record NativeRichDocument(
    string Text,
    IReadOnlyList<NativeTextSpan> TextSpans,
    IReadOnlyList<NativeParagraphSpan> ParagraphSpans);

internal static partial class MarkdownRichText
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-+*]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*(\d+)\.\s+(.*)$")]
    private static partial Regex OrderedRegex();

    [GeneratedRegex("^<p\\s+align=[\"']center[\"']>(.*)</p>$", RegexOptions.IgnoreCase)]
    private static partial Regex CenteredParagraphRegex();

    [GeneratedRegex("^<video(?:\\s+controls)?\\s+src=[\"']([^\"']+)[\"'][^>]*></video>$", RegexOptions.IgnoreCase)]
    private static partial Regex VideoRegex();

    public static NativeRichDocument Parse(string markdown)
    {
        markdown = Normalize(markdown);
        var output = new StringBuilder();
        var textSpans = new List<NativeTextSpan>();
        var paragraphs = new List<NativeParagraphSpan>();
        var inCodeBlock = false;
        var lines = markdown.Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var source = lines[lineIndex];
            if (source.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            var paragraphStart = output.Length;
            var kind = inCodeBlock ? NativeParagraphKind.CodeBlock : NativeParagraphKind.Normal;
            var alignment = 0;
            if (!inCodeBlock)
            {
                var centered = CenteredParagraphRegex().Match(source);
                if (centered.Success)
                {
                    source = centered.Groups[1].Value;
                    alignment = 1;
                }
                var heading = HeadingRegex().Match(source);
                var bullet = BulletRegex().Match(source);
                var ordered = OrderedRegex().Match(source);
                if (heading.Success)
                {
                    kind = heading.Groups[1].Length switch
                    {
                        1 => NativeParagraphKind.Heading1,
                        2 => NativeParagraphKind.Heading2,
                        _ => NativeParagraphKind.Heading3,
                    };
                    source = heading.Groups[2].Value;
                }
                else if (bullet.Success)
                {
                    kind = NativeParagraphKind.Bullet;
                    source = bullet.Groups[1].Value;
                }
                else if (ordered.Success)
                {
                    kind = NativeParagraphKind.Ordered;
                    source = ordered.Groups[2].Value;
                }
                else if (source.StartsWith("> ", StringComparison.Ordinal) || source == ">")
                {
                    kind = NativeParagraphKind.Quote;
                    source = source.Length > 1 ? source[2..] : string.Empty;
                }
                else if (source.Trim() is "---" or "***" or "___")
                {
                    kind = NativeParagraphKind.HorizontalRule;
                    source = "────────────────";
                }
            }

            if (inCodeBlock)
            {
                output.Append(source);
                if (source.Length > 0)
                {
                    textSpans.Add(new NativeTextSpan(paragraphStart, source.Length, NativeTextStyle.Code));
                }
            }
            else
            {
                ParseInline(source, output, textSpans);
            }

            var paragraphLength = output.Length - paragraphStart;
            paragraphs.Add(new NativeParagraphSpan(paragraphStart, paragraphLength, kind, alignment));
            if (lineIndex < lines.Length - 1)
            {
                output.Append('\n');
            }
        }

        return new NativeRichDocument(output.ToString(), textSpans, paragraphs);
    }

    private static void ParseInline(string source, StringBuilder output, List<NativeTextSpan> spans)
    {
        var index = 0;
        while (index < source.Length)
        {
            if (TryVideo(source, ref index, output, spans) || TryFormula(source, ref index, output, spans)
                || TryImage(source, ref index, output, spans) || TryLink(source, ref index, output, spans)
                || TryDelimited(source, ref index, output, spans, "***", NativeTextStyle.Bold | NativeTextStyle.Italic)
                || TryDelimited(source, ref index, output, spans, "___", NativeTextStyle.Bold | NativeTextStyle.Italic)
                || TryDelimited(source, ref index, output, spans, "**", NativeTextStyle.Bold)
                || TryDelimited(source, ref index, output, spans, "__", NativeTextStyle.Bold)
                || TryDelimited(source, ref index, output, spans, "<u>", "</u>", NativeTextStyle.Underline)
                || TryDelimited(source, ref index, output, spans, "`", NativeTextStyle.Code)
                || TryDelimited(source, ref index, output, spans, "*", NativeTextStyle.Italic)
                || TryDelimited(source, ref index, output, spans, "_", NativeTextStyle.Italic))
            {
                continue;
            }

            if (source[index] == '\\' && index + 1 < source.Length)
            {
                output.Append(source[index + 1]);
                index += 2;
            }
            else
            {
                output.Append(source[index++]);
            }
        }
    }
    private static bool TryVideo(string source, ref int index, StringBuilder output, List<NativeTextSpan> spans)
    {
        if (index != 0) return false;
        var match = VideoRegex().Match(source);
        if (!match.Success) return false;
        const string label = "▶ Video";
        var start = output.Length;
        output.Append(label);
        spans.Add(new NativeTextSpan(start, label.Length, NativeTextStyle.Underline, $"video:{match.Groups[1].Value}"));
        index = source.Length;
        return true;
    }

    private static bool TryFormula(string source, ref int index, StringBuilder output, List<NativeTextSpan> spans)
    {
        if (source[index] != '$' || index + 1 >= source.Length) return false;
        var end = source.IndexOf('$', index + 1);
        if (end < 0) return false;
        var value = source[(index + 1)..end];
        var start = output.Length;
        output.Append(value);
        spans.Add(new NativeTextSpan(start, value.Length, NativeTextStyle.Code, $"formula:{value}"));
        index = end + 1;
        return true;
    }

    private static bool TryImage(string source, ref int index, StringBuilder output, List<NativeTextSpan> spans)
    {
        if (!source.AsSpan(index).StartsWith("![", StringComparison.Ordinal))
        {
            return false;
        }
        var labelEnd = source.IndexOf("](", index + 2, StringComparison.Ordinal);
        var urlEnd = labelEnd < 0 ? -1 : source.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0)
        {
            return false;
        }
        var label = source[(index + 2)..labelEnd];
        var url = source[(labelEnd + 2)..urlEnd];
        var start = output.Length;
        output.Append("🖼 ").Append(string.IsNullOrWhiteSpace(label) ? "Image" : label);
        spans.Add(new NativeTextSpan(start, output.Length - start, NativeTextStyle.Italic, $"image:{url}"));
        index = urlEnd + 1;
        return true;
    }

    private static bool TryLink(string source, ref int index, StringBuilder output, List<NativeTextSpan> spans)
    {
        if (source[index] != '[')
        {
            return false;
        }
        var labelEnd = source.IndexOf("](", index + 1, StringComparison.Ordinal);
        var urlEnd = labelEnd < 0 ? -1 : source.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0)
        {
            return false;
        }
        var label = source[(index + 1)..labelEnd];
        var start = output.Length;
        output.Append(label);
        spans.Add(new NativeTextSpan(start, label.Length, NativeTextStyle.Underline, source[(labelEnd + 2)..urlEnd]));
        index = urlEnd + 1;
        return true;
    }

    private static bool TryDelimited(string source, ref int index, StringBuilder output,
        List<NativeTextSpan> spans, string marker, NativeTextStyle style) =>
        TryDelimited(source, ref index, output, spans, marker, marker, style);

    private static bool TryDelimited(string source, ref int index, StringBuilder output,
        List<NativeTextSpan> spans, string open, string close, NativeTextStyle style)
    {
        if (!source.AsSpan(index).StartsWith(open, StringComparison.Ordinal))
        {
            return false;
        }
        var end = source.IndexOf(close, index + open.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }
        var start = output.Length;
        var content = source[(index + open.Length)..end];
        if (style == NativeTextStyle.Code)
        {
            output.Append(content);
        }
        else
        {
            ParseInline(content, output, spans);
        }
        if (output.Length > start)
        {
            spans.Add(new NativeTextSpan(start, output.Length - start, style));
        }
        index = end + close.Length;
        return true;
    }

    public static string Serialize(string text, IReadOnlyList<NativeTextStyle> styles,
        IReadOnlyList<NativeParagraphSpan> paragraphs, IReadOnlyList<NativeTextSpan> semanticSpans)
    {
        text = Normalize(text);
        var output = new StringBuilder();
        var lines = GetLines(text);
        var codeBlockOpen = false;
        foreach (var line in lines)
        {
            var paragraph = paragraphs.FirstOrDefault(item => item.Start <= line.Start
                && item.Start + Math.Max(1, item.Length) >= line.Start);
            var kind = paragraph?.Kind ?? NativeParagraphKind.Normal;
            if (kind == NativeParagraphKind.CodeBlock && !codeBlockOpen)
            {
                output.Append("```\n");
                codeBlockOpen = true;
            }
            else if (kind != NativeParagraphKind.CodeBlock && codeBlockOpen)
            {
                output.Append("```\n");
                codeBlockOpen = false;
            }

            var contentStart = line.Start;
            var contentLength = line.Length;
            var centered = paragraph?.Alignment == 1 && kind != NativeParagraphKind.CodeBlock;
            if (centered) output.Append("<p align=\"center\">");
            switch (kind)
            {
                case NativeParagraphKind.Heading1: output.Append("# "); break;
                case NativeParagraphKind.Heading2: output.Append("## "); break;
                case NativeParagraphKind.Heading3: output.Append("### "); break;
                case NativeParagraphKind.Quote: output.Append("> "); break;
                case NativeParagraphKind.Bullet:
                    output.Append("- ");
                    break;
                case NativeParagraphKind.Ordered:
                    output.Append("1. ");
                    break;
                case NativeParagraphKind.HorizontalRule:
                    output.Append("---");
                    contentLength = 0;
                    break;
            }
            AppendInline(output, text, contentStart, Math.Max(0, contentLength), styles, semanticSpans,
                kind is NativeParagraphKind.Heading1 or NativeParagraphKind.Heading2 or NativeParagraphKind.Heading3);
            if (centered) output.Append("</p>");
            if (line.HasNewLine) output.Append('\n');
        }
        if (codeBlockOpen)
        {
            if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
            output.Append("```");
        }
        return output.ToString();
    }
    private static void AppendInline(StringBuilder output, string text, int start, int length,
        IReadOnlyList<NativeTextStyle> styles, IReadOnlyList<NativeTextSpan> semanticSpans, bool heading)
    {
        var end = Math.Min(text.Length, start + length);
        var at = start;
        while (at < end)
        {
            var semantic = semanticSpans.FirstOrDefault(span => span.Link is not null
                && span.Start <= at && span.Start + span.Length > at);
            var style = at < styles.Count ? styles[at] : NativeTextStyle.None;
            if (heading) style &= ~NativeTextStyle.Bold;
            var runEnd = at + 1;
            while (runEnd < end
                && (runEnd < styles.Count ? styles[runEnd] : NativeTextStyle.None) == (at < styles.Count ? styles[at] : NativeTextStyle.None)
                && ReferenceEquals(semantic, semanticSpans.FirstOrDefault(span => span.Link is not null
                    && span.Start <= runEnd && span.Start + span.Length > runEnd)))
            {
                runEnd++;
            }
            var value = text[at..runEnd];
            if (semantic?.Link?.StartsWith("image:", StringComparison.Ordinal) == true)
            {
                var label = value.StartsWith("🖼 ", StringComparison.Ordinal) ? value[3..] : value;
                output.Append("![").Append(label).Append("](").Append(semantic.Link[6..]).Append(')');
            }
            else if (semantic?.Link?.StartsWith("video:", StringComparison.Ordinal) == true)
            {
                output.Append("<video controls src=\"").Append(semantic.Link[6..]).Append("\"></video>");
            }
            else if (semantic?.Link?.StartsWith("formula:", StringComparison.Ordinal) == true)
            {
                output.Append('$').Append(value).Append('$');
            }
            else
            {
                if (style.HasFlag(NativeTextStyle.Code)) output.Append('`');
                if (style.HasFlag(NativeTextStyle.Bold)) output.Append("**");
                if (style.HasFlag(NativeTextStyle.Italic)) output.Append('*');
                if (style.HasFlag(NativeTextStyle.Underline) && semantic?.Link is null) output.Append("<u>");
                if (semantic?.Link is not null) output.Append('[');
                output.Append(value);
                if (semantic?.Link is not null) output.Append("](").Append(semantic.Link).Append(')');
                if (style.HasFlag(NativeTextStyle.Underline) && semantic?.Link is null) output.Append("</u>");
                if (style.HasFlag(NativeTextStyle.Italic)) output.Append('*');
                if (style.HasFlag(NativeTextStyle.Bold)) output.Append("**");
                if (style.HasFlag(NativeTextStyle.Code)) output.Append('`');
            }
            at = runEnd;
        }
    }

    public static IReadOnlyList<NativeTextSpan> AdjustSemanticSpans(
        IReadOnlyList<NativeTextSpan> spans, string oldText, string newText)
    {
        if (spans.Count == 0 || oldText == newText) return spans;
        var prefix = 0;
        while (prefix < oldText.Length && prefix < newText.Length && oldText[prefix] == newText[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldText.Length - prefix && suffix < newText.Length - prefix
            && oldText[^(suffix + 1)] == newText[^(suffix + 1)]) suffix++;
        var oldEnd = oldText.Length - suffix;
        var delta = newText.Length - oldText.Length;
        var adjusted = new List<NativeTextSpan>();
        foreach (var span in spans.Where(item => item.Link is not null))
        {
            if (span.Start >= oldEnd) adjusted.Add(span with { Start = Math.Max(0, span.Start + delta) });
            else if (span.Start + span.Length <= prefix) adjusted.Add(span);
            else
            {
                var newLength = Math.Max(0, span.Length + delta);
                if (newLength > 0) adjusted.Add(span with { Length = newLength });
            }
        }
        return adjusted;
    }

    public static IReadOnlyList<(int Start, int Length, bool HasNewLine)> GetLines(string text)
    {
        var lines = new List<(int, int, bool)>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n') continue;
            lines.Add((start, index - start, true));
            start = index + 1;
        }
        lines.Add((start, text.Length - start, false));
        return lines;
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n');
}