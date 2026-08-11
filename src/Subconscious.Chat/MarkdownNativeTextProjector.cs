using System.Text;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Subconscious.Chat;

internal static class MarkdownNativeTextProjector
{
    public static MarkdownTextDocument Project(MarkdownDocument document, string source)
    {
        var writer = new Writer(source);
        foreach (var block in document)
        {
            writer.AppendBlock(block, 0, MarkdownTextStyle.None);
        }
        return writer.Build();
    }

    private sealed class Writer(string source)
    {
        private readonly string _source = source;
        private readonly StringBuilder _text = new();
        private readonly List<MarkdownTextSpan> _spans = [];

        public MarkdownTextDocument Build()
        {
            while (_text.Length > 0 && _text[^1] == '\n')
            {
                _text.Length--;
            }
            var length = _text.Length;
            var spans = _spans
                .Where(span => span.Start < length)
                .Select(span => span with { Length = Math.Min(span.Length, length - span.Start) })
                .Where(span => span.Length > 0)
                .ToArray();
            return new MarkdownTextDocument(_text.ToString(), spans);
        }

        public void AppendBlock(Block block, int depth, MarkdownTextStyle inherited)
        {
            switch (block)
            {
                case Table table:
                    AppendTable(table, inherited);
                    break;
                case ListBlock list:
                    AppendList(list, depth, inherited);
                    break;
                case QuoteBlock quote:
                    Append("> ", inherited | MarkdownTextStyle.Quote);
                    foreach (var child in quote)
                    {
                        AppendBlock(child, depth, inherited | MarkdownTextStyle.Quote);
                    }
                    EnsureNewlines(2);
                    break;
                case CodeBlock code:
                    Append(code.Lines.ToString(), inherited | MarkdownTextStyle.CodeBlock);
                    EnsureNewlines(2);
                    break;
                case HeadingBlock heading:
                    AppendInline(heading.Inline, inherited | HeadingStyle(heading.Level), null);
                    EnsureNewlines(2);
                    break;
                case ParagraphBlock paragraph:
                    AppendInline(paragraph.Inline, inherited, null);
                    EnsureNewlines(2);
                    break;
                case ThematicBreakBlock:
                    Append("———", inherited);
                    EnsureNewlines(2);
                    break;
                case ContainerBlock container:
                    foreach (var child in container)
                    {
                        AppendBlock(child, depth, inherited);
                    }
                    break;
                case LeafBlock leaf:
                    AppendInline(leaf.Inline, inherited, null);
                    EnsureNewlines(2);
                    break;
                default:
                    AppendSource(block.Span.Start, block.Span.End, inherited);
                    EnsureNewlines(2);
                    break;
            }
        }

        private void AppendList(ListBlock list, int depth, MarkdownTextStyle inherited)
        {
            var number = int.TryParse(list.OrderedStart, out var orderedStart) ? orderedStart : 1;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var marker = list.IsOrdered ? $"{number++}. " : "- ";
                Append(new string(' ', depth * 2) + marker, inherited);
                var first = true;
                foreach (var child in item)
                {
                    if (!first)
                    {
                        EnsureNewlines(1);
                        if (child is not ListBlock)
                        {
                            Append(new string(' ', (depth * 2) + marker.Length), inherited);
                        }
                    }
                    if (child is ParagraphBlock paragraph)
                    {
                        AppendInline(paragraph.Inline, inherited, null);
                    }
                    else
                    {
                        AppendBlock(child, depth + 1, inherited);
                    }
                    first = false;
                }
                EnsureNewlines(1);
            }
            EnsureNewlines(2);
        }

        private void AppendTable(Table table, MarkdownTextStyle inherited)
        {
            foreach (var row in table.OfType<TableRow>())
            {
                var cellStyle = row.IsHeader ? inherited | MarkdownTextStyle.TableHeader : inherited;
                var firstCell = true;
                foreach (var cell in row.OfType<TableCell>())
                {
                    if (!firstCell)
                    {
                        Append("  |  ", inherited);
                    }
                    AppendTableCell(cell, cellStyle);
                    firstCell = false;
                }
                EnsureNewlines(1);
            }
            EnsureNewlines(2);
        }

        private void AppendTableCell(TableCell cell, MarkdownTextStyle style)
        {
            foreach (var child in cell)
            {
                if (child is ParagraphBlock paragraph)
                {
                    AppendInline(paragraph.Inline, style, null);
                }
                else
                {
                    AppendBlock(child, 0, style);
                }
            }
        }

        private void AppendInline(ContainerInline? container, MarkdownTextStyle inherited, string? linkTarget)
        {
            var inline = container?.FirstChild;
            while (inline is not null)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        Append(literal.Content.ToString(), inherited, linkTarget);
                        break;
                    case CodeInline code:
                        Append(code.Content, inherited | MarkdownTextStyle.Code, linkTarget);
                        break;
                    case LineBreakInline:
                        EnsureNewlines(1);
                        break;
                    case AutolinkInline autoLink:
                        Append(autoLink.Url, inherited | MarkdownTextStyle.Link, autoLink.Url);
                        break;
                    case LinkInline link:
                        AppendInline(link, inherited | MarkdownTextStyle.Link, link.Url);
                        break;
                    case EmphasisInline emphasis:
                        var emphasisStyle = emphasis.DelimiterCount >= 2
                            ? MarkdownTextStyle.Strong
                            : MarkdownTextStyle.Emphasis;
                        AppendInline(emphasis, inherited | emphasisStyle, linkTarget);
                        break;
                    case HtmlInline:
                        break;
                    case ContainerInline nested:
                        AppendInline(nested, inherited, linkTarget);
                        break;
                    default:
                        AppendSource(inline.Span.Start, inline.Span.End, inherited, linkTarget);
                        break;
                }
                inline = inline.NextSibling;
            }
        }

        private void Append(string? value, MarkdownTextStyle style, string? linkTarget = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            var normalized = value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            var start = _text.Length;
            _text.Append(normalized);
            if (style == MarkdownTextStyle.None)
            {
                return;
            }

            var span = new MarkdownTextSpan(start, normalized.Length, style, linkTarget);
            if (_spans.Count > 0)
            {
                var previous = _spans[^1];
                if (previous.End == span.Start && previous.Style == span.Style
                    && string.Equals(previous.LinkTarget, span.LinkTarget, StringComparison.Ordinal))
                {
                    _spans[^1] = previous with { Length = previous.Length + span.Length };
                    return;
                }
            }
            _spans.Add(span);
        }

        private void AppendSource(int start, int end, MarkdownTextStyle style, string? linkTarget = null)
        {
            if (start < 0 || end < start || start >= _source.Length)
            {
                return;
            }
            var length = Math.Min(_source.Length - start, end - start + 1);
            Append(_source.Substring(start, length), style, linkTarget);
        }

        private void EnsureNewlines(int count)
        {
            var existing = 0;
            for (var index = _text.Length - 1; index >= 0 && _text[index] == '\n'; index--)
            {
                existing++;
            }
            if (existing < count)
            {
                _text.Append('\n', count - existing);
            }
        }

        private static MarkdownTextStyle HeadingStyle(int level) => level switch
        {
            1 => MarkdownTextStyle.Heading1,
            2 => MarkdownTextStyle.Heading2,
            3 => MarkdownTextStyle.Heading3,
            4 => MarkdownTextStyle.Heading4,
            5 => MarkdownTextStyle.Heading5,
            _ => MarkdownTextStyle.Heading6,
        };
    }
}
