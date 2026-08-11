using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Subconscious.Chat;

/// <summary>Creates safe HTML and layout-friendly plain text from untrusted Markdown.</summary>
public sealed class MarkdownProjectionService
{
    private static readonly MarkdownPipeline SafePipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static MarkdownProjectionService Default { get; } = new();

    public MarkdownProjection Project(string? markdown)
    {
        var source = markdown ?? string.Empty;
        var document = Markdown.Parse(source, SafePipeline);
        var nativeText = MarkdownNativeTextProjector.Project(document, source);
        return new MarkdownProjection(
            source,
            Markdown.ToHtml(document, SafePipeline),
            nativeText.Text.Replace("\n", Environment.NewLine, StringComparison.Ordinal),
            nativeText);
    }

    public string ToSafeHtml(string? markdown) => Project(markdown).Html;

    public string ToPlainText(string? markdown) => Project(markdown).PlainText;

    private static class MarkdownPlainText
    {
        public static string Project(MarkdownDocument document)
        {
            var output = new StringBuilder();
            foreach (var block in document)
            {
                AppendBlock(output, block, 0);
            }

            return Normalize(output.ToString());
        }

        private static void AppendBlock(StringBuilder output, Block block, int depth)
        {
            switch (block)
            {
                case Table table:
                    AppendTable(output, table);
                    break;
                case ListBlock list:
                    AppendList(output, list, depth);
                    break;
                case QuoteBlock quote:
                    AppendQuote(output, quote, depth);
                    break;
                case CodeBlock code:
                    AppendCode(output, code);
                    break;
                case HeadingBlock heading:
                    AppendInline(output, heading.Inline);
                    AppendParagraphBreak(output);
                    break;
                case ParagraphBlock paragraph:
                    AppendInline(output, paragraph.Inline);
                    AppendParagraphBreak(output);
                    break;
                case ThematicBreakBlock:
                    output.AppendLine("———");
                    AppendParagraphBreak(output);
                    break;
                case ContainerBlock container:
                    foreach (var child in container)
                    {
                        AppendBlock(output, child, depth);
                    }
                    break;
                case LeafBlock leaf:
                    AppendInline(output, leaf.Inline);
                    AppendParagraphBreak(output);
                    break;
            }
        }

        private static void AppendList(StringBuilder output, ListBlock list, int depth)
        {
            var number = 1;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                var itemText = new StringBuilder();
                foreach (var child in item)
                {
                    AppendBlock(itemText, child, depth + 1);
                }

                var lines = Normalize(itemText.ToString()).Split(Environment.NewLine);
                var marker = list.IsOrdered ? $"{number++}." : "-";
                var indentation = new string(' ', depth * 2);
                output.Append(indentation).Append(marker).Append(' ');
                output.AppendLine(lines.FirstOrDefault() ?? string.Empty);
                foreach (var line in lines.Skip(1))
                {
                    output.Append(indentation)
                        .Append(' ', marker.Length + 1)
                        .AppendLine(line);
                }
            }

            AppendParagraphBreak(output);
        }

        private static void AppendQuote(StringBuilder output, QuoteBlock quote, int depth)
        {
            var quoteText = new StringBuilder();
            foreach (var child in quote)
            {
                AppendBlock(quoteText, child, depth);
            }

            foreach (var line in Normalize(quoteText.ToString()).Split(Environment.NewLine))
            {
                output.Append("> ").AppendLine(line);
            }

            AppendParagraphBreak(output);
        }

        private static void AppendCode(StringBuilder output, CodeBlock code)
        {
            output.AppendLine(code.Lines.ToString());
            AppendParagraphBreak(output);
        }

        private static void AppendTable(StringBuilder output, Table table)
        {
            foreach (var row in table.OfType<TableRow>())
            {
                var cells = new List<string>();
                foreach (var cell in row.OfType<TableCell>())
                {
                    var cellText = new StringBuilder();
                    foreach (var child in cell)
                    {
                        AppendBlock(cellText, child, 0);
                    }

                    cells.Add(Normalize(cellText.ToString())
                        .Replace(Environment.NewLine, " ", StringComparison.Ordinal));
                }

                output.AppendLine(string.Join('\t', cells));
            }

            AppendParagraphBreak(output);
        }

        private static void AppendInline(StringBuilder output, ContainerInline? container)
        {
            var inline = container?.FirstChild;
            while (inline is not null)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        output.Append(literal.Content);
                        break;
                    case CodeInline code:
                        output.Append(code.Content);
                        break;
                    case LineBreakInline:
                        output.AppendLine();
                        break;
                    case AutolinkInline autoLink:
                        output.Append(autoLink.Url);
                        break;
                    case HtmlInline:
                        // Raw HTML is intentionally omitted from selectable output.
                        break;
                    case ContainerInline nested:
                        AppendInline(output, nested);
                        break;
                }

                inline = inline.NextSibling;
            }
        }

        private static void AppendParagraphBreak(StringBuilder output)
        {
            if (output.Length > 0 && output[^1] != '\n')
            {
                output.AppendLine();
            }

            output.AppendLine();
        }

        private static string Normalize(string text)
        {
            var normalized = text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();

            while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
            }

            return normalized.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }
    }
}
