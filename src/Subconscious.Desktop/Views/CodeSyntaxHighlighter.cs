namespace Subconscious.Desktop.Views;

/// <summary>Applies compact, theme-aware token coloring to native Markdown code labels.</summary>
internal static class CodeSyntaxHighlighter
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "async", "await", "bool", "class", "const", "decimal", "else", "enum", "false",
        "foreach", "if", "in", "int", "interface", "internal", "new", "null", "private", "public",
        "readonly", "return", "static", "string", "task", "this", "true", "using", "var", "void",
        "while",
    };

    private static readonly HashSet<string> ScriptKeywords = new(StringComparer.Ordinal)
    {
        "async", "await", "break", "case", "catch", "class", "const", "continue", "def", "else", "false",
        "for", "from", "function", "if", "import", "in", "let", "new", "none", "null", "return", "true",
        "try", "undefined", "var", "while", "with", "yield",
    };

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "by", "create", "delete", "drop", "from", "group", "insert", "into", "join", "limit",
        "not", "null", "on", "or", "order", "select", "set", "table", "update", "values", "where",
    };

    public static Label CreateLabel(string code, string? language)
    {
        var label = new Label
        {
            FontFamily = "monospace",
            LineBreakMode = LineBreakMode.NoWrap,
        };
        label.SetDynamicResource(Label.TextColorProperty, "PrimaryTextColor");

        var normalizedLanguage = NormalizeLanguage(language);
        if (normalizedLanguage is null)
        {
            label.Text = code;
            return label;
        }

        var formatted = new FormattedString();
        AppendHighlightedCode(formatted, code, normalizedLanguage);
        label.FormattedText = formatted;
        return label;
    }

    public static string? GuessLanguage(string code)
    {
        var trimmed = code.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return "json";
        }

        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            return "xml";
        }

        return code.Contains("public class", StringComparison.Ordinal)
            || code.Contains("using ", StringComparison.Ordinal)
            || code.Contains("private ", StringComparison.Ordinal)
            ? "csharp"
            : null;
    }

    private static string? NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        "cs" or "c#" or "csharp" => "csharp",
        "json" => "json",
        "xml" or "html" or "xhtml" or "xaml" => "markup",
        "js" or "javascript" or "ts" or "typescript" => "script",
        "py" or "python" => "python",
        "sh" or "bash" or "shell" or "ps1" or "powershell" => "shell",
        "sql" => "sql",
        _ => null,
    };

    private static void AppendHighlightedCode(FormattedString target, string code, string language)
    {
        var plainStart = 0;
        for (var index = 0; index < code.Length;)
        {
            var tokenStart = index;
            string? resourceKey = null;

            if (TryConsumeComment(code, language, ref index))
            {
                resourceKey = "SecondaryTextColor";
            }
            else if (TryConsumeString(code, ref index))
            {
                resourceKey = "ErrorColor";
            }
            else if (TryConsumeMarkupTag(code, language, ref index))
            {
                resourceKey = "AccentColor";
            }
            else if (TryConsumeNumber(code, ref index))
            {
                resourceKey = "ErrorColor";
            }
            else if (TryConsumeIdentifier(code, ref index, out var identifier)
                     && IsKeyword(identifier, language))
            {
                resourceKey = "AccentColor";
            }

            if (resourceKey is null)
            {
                index = Math.Max(index, tokenStart + 1);
                continue;
            }

            AppendSpan(target, code[plainStart..tokenStart], "PrimaryTextColor");
            AppendSpan(target, code[tokenStart..index], resourceKey);
            plainStart = index;
        }

        AppendSpan(target, code[plainStart..], "PrimaryTextColor");
    }

    private static bool TryConsumeComment(string code, string language, ref int index)
    {
        var supportsSlashComments = language is "csharp" or "script" or "sql";
        if (supportsSlashComments && index + 1 < code.Length && code[index] == '/' && code[index + 1] == '/')
        {
            index = ConsumeToLineEnd(code, index + 2);
            return true;
        }

        if (supportsSlashComments && index + 1 < code.Length && code[index] == '/' && code[index + 1] == '*')
        {
            var close = code.IndexOf("*/", index + 2, StringComparison.Ordinal);
            index = close < 0 ? code.Length : close + 2;
            return true;
        }

        if (language is "python" or "shell" && code[index] == '#')
        {
            index = ConsumeToLineEnd(code, index + 1);
            return true;
        }

        if (language == "sql" && index + 1 < code.Length && code[index] == '-' && code[index + 1] == '-')
        {
            index = ConsumeToLineEnd(code, index + 2);
            return true;
        }

        return false;
    }

    private static bool TryConsumeString(string code, ref int index)
    {
        var quoteIndex = index;
        if (code[index] == '@' && index + 1 < code.Length && code[index + 1] == '"')
        {
            quoteIndex++;
        }

        if (code[quoteIndex] is not ('\'' or '"'))
        {
            return false;
        }

        var quote = code[quoteIndex];
        index = quoteIndex + 1;
        while (index < code.Length)
        {
            if (code[index] == '\\' && index + 1 < code.Length)
            {
                index += 2;
                continue;
            }

            if (code[index++] == quote)
            {
                break;
            }
        }

        return true;
    }

    private static bool TryConsumeMarkupTag(string code, string language, ref int index)
    {
        if (language != "markup" || code[index] != '<')
        {
            return false;
        }

        var close = code.IndexOf('>', index + 1);
        index = close < 0 ? code.Length : close + 1;
        return true;
    }

    private static bool TryConsumeNumber(string code, ref int index)
    {
        if (!char.IsDigit(code[index]))
        {
            return false;
        }

        index++;
        while (index < code.Length && (char.IsDigit(code[index]) || code[index] is '.' or '_' or 'x' or 'X' or 'a' or 'b' or 'c' or 'd' or 'e' or 'f' or 'A' or 'B' or 'C' or 'D' or 'E' or 'F'))
        {
            index++;
        }

        return true;
    }

    private static bool TryConsumeIdentifier(string code, ref int index, out string identifier)
    {
        if (!IsIdentifierStart(code[index]))
        {
            identifier = string.Empty;
            return false;
        }

        var start = index++;
        while (index < code.Length && IsIdentifierPart(code[index]))
        {
            index++;
        }

        identifier = code[start..index];
        return true;
    }

    private static bool IsKeyword(string identifier, string language) => language switch
    {
        "csharp" => CSharpKeywords.Contains(identifier),
        "script" or "python" or "shell" => ScriptKeywords.Contains(identifier),
        "json" => identifier is "true" or "false" or "null",
        "sql" => SqlKeywords.Contains(identifier.ToLowerInvariant()),
        _ => false,
    };

    private static int ConsumeToLineEnd(string code, int index)
    {
        while (index < code.Length && code[index] is not '\r' and not '\n')
        {
            index++;
        }

        return index;
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static void AppendSpan(FormattedString target, string text, string resourceKey)
    {
        if (text.Length == 0)
        {
            return;
        }

        var span = new Span { Text = text, FontFamily = "monospace" };
        span.SetDynamicResource(Span.TextColorProperty, resourceKey);
        target.Spans.Add(span);
    }
}
