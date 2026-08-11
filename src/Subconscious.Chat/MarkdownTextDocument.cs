namespace Subconscious.Chat;

[Flags]
public enum MarkdownTextStyle
{
    None = 0,
    Heading1 = 1 << 0,
    Heading2 = 1 << 1,
    Heading3 = 1 << 2,
    Heading4 = 1 << 3,
    Heading5 = 1 << 4,
    Heading6 = 1 << 5,
    Strong = 1 << 6,
    Emphasis = 1 << 7,
    Code = 1 << 8,
    CodeBlock = 1 << 9,
    Link = 1 << 10,
    Quote = 1 << 11,
    TableHeader = 1 << 12,
}

public sealed record MarkdownTextSpan(
    int Start,
    int Length,
    MarkdownTextStyle Style,
    string? LinkTarget = null)
{
    public int End => Start + Length;
}

public sealed record MarkdownTextDocument(
    string Text,
    IReadOnlyList<MarkdownTextSpan> Spans);