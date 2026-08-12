using System.Text;

namespace Subconscious.WYSIWYG;

/// <summary>Builds one atomic RichEdit presentation for highlighted code.</summary>
internal static class CodeRtfFormatter
{
    public static string Build(string source, IReadOnlyList<NativeTextSpan> spans,
        EditorTheme theme, float pointSize)
    {
        var output = new StringBuilder(source.Length + (spans.Count * 16) + 256);
        output.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Cascadia Mono;}}{\colortbl ;");
        AppendColor(output, theme.Text);
        AppendColor(output, theme.SyntaxKeyword);
        AppendColor(output, theme.SyntaxString);
        AppendColor(output, theme.SyntaxNumber);
        AppendColor(output, theme.MutedText);
        output.Append(@"}\viewkind4\uc1\pard\f0\fs")
            .Append(Math.Max(1, (int)Math.Round(pointSize * 2))).Append(@"\cf1 ");

        var position = 0;
        foreach (var span in spans)
        {
            var start = Math.Clamp(span.Start, position, source.Length);
            var end = Math.Clamp(span.Start + span.Length, start, source.Length);
            AppendText(output, source.AsSpan(position, start - position));
            output.Append("\\cf").Append(ColorIndex(span.Link));
            if (span.Style.HasFlag(NativeTextStyle.Italic)) output.Append(@"\i");
            output.Append(' ');
            AppendText(output, source.AsSpan(start, end - start));
            output.Append(@"\cf1\i0 ");
            position = end;
        }
        AppendText(output, source.AsSpan(position));
        return output.Append('}').ToString();
    }

    private static int ColorIndex(string? token) => token switch
    {
        "syntax:keyword" => 2,
        "syntax:string" => 3,
        "syntax:number" => 4,
        "syntax:comment" => 5,
        _ => 1,
    };

    private static void AppendColor(StringBuilder output, Color color)
    {
        output.Append("\\red").Append((byte)Math.Round(color.Red * 255))
            .Append("\\green").Append((byte)Math.Round(color.Green * 255))
            .Append("\\blue").Append((byte)Math.Round(color.Blue * 255)).Append(';');
    }

    private static void AppendText(StringBuilder output, ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\': output.Append(@"\\"); break;
                case '{': output.Append(@"\{"); break;
                case '}': output.Append(@"\}"); break;
                case '\r': break;
                case '\n': output.Append("\\par\n"); break;
                case '\t': output.Append(@"\tab "); break;
                default:
                    if (character is >= ' ' and <= '~') output.Append(character);
                    else output.Append("\\u").Append(unchecked((short)character)).Append('?');
                    break;
            }
        }
    }
}
