using System.Globalization;

namespace Subconscious.Engine.Tools.Builtin;

/// <summary>Raised for any malformed or disallowed expression. Message is surfaced to the model.</summary>
public sealed class ExpressionException(string message) : Exception(message);

/// <summary>
/// Evaluates arithmetic expressions with no code-execution surface. Counterpart of
/// <c>tools/calculator.py</c>'s <c>_eval_node</c>, which walked a Python <c>ast</c> restricted to
/// a whitelist of operators and functions specifically to avoid <c>eval()</c>.
///
/// <para>
/// .NET has no equivalent "parse but don't execute" primitive for this
/// (<c>DataTable.Compute</c> supports a different, locale-sensitive grammar; a scripting engine
/// would reintroduce exactly the arbitrary-code-execution risk the Python version was written to
/// avoid), so this is a purpose-built tokenizer plus recursive-descent parser. There is no
/// reflection, no dynamic dispatch and no identifier resolution outside
/// <see cref="Functions"/>/<see cref="Constants"/>, so an untrusted expression can at worst
/// produce a wrong number or an error string.
/// </para>
///
/// <para>
/// Python operator semantics are reproduced deliberately, since the tool's documented examples
/// come from Python: <c>/</c> is true division, <c>//</c> is floor division, <c>%</c> takes the
/// sign of the divisor (unlike .NET's <c>%</c>), <c>**</c> is right-associative and binds tighter
/// than unary minus (so <c>-2**2</c> is -4), and division by zero is an error rather than
/// infinity.
/// </para>
/// </summary>
public static class SafeExpressionEvaluator
{
    private static readonly Dictionary<string, double> Constants = new(StringComparer.Ordinal)
    {
        ["pi"] = Math.PI,
        ["e"] = Math.E,
        ["tau"] = Math.Tau,
        ["inf"] = double.PositiveInfinity,
    };

    /// <summary>Whitelisted functions: name to (minimum arity, maximum arity, implementation).</summary>
    private static readonly Dictionary<string, (int MinArgs, int MaxArgs, Func<double[], double> Invoke)> Functions =
        new(StringComparer.Ordinal)
        {
            ["abs"] = (1, 1, a => Math.Abs(a[0])),
            // Python's round() is banker's rounding, which is also Math.Round's default mode.
            ["round"] = (1, 2, a => Math.Round(a[0], a.Length > 1 ? RequireDigits(a[1]) : 0, MidpointRounding.ToEven)),
            ["sqrt"] = (1, 1, a => Math.Sqrt(RequireNonNegative(a[0], "sqrt"))),
            ["cbrt"] = (1, 1, a => Math.Pow(a[0], 1.0 / 3.0)),
            ["exp"] = (1, 1, a => Math.Exp(a[0])),
            // log(x) is natural; log(x, base) mirrors math.log's optional second argument.
            ["log"] = (1, 2, a => a.Length > 1 ? Math.Log(a[0]) / Math.Log(a[1]) : Math.Log(a[0])),
            ["log2"] = (1, 1, a => Math.Log2(a[0])),
            ["log10"] = (1, 1, a => Math.Log10(a[0])),
            ["sin"] = (1, 1, a => Math.Sin(a[0])),
            ["cos"] = (1, 1, a => Math.Cos(a[0])),
            ["tan"] = (1, 1, a => Math.Tan(a[0])),
            ["asin"] = (1, 1, a => Math.Asin(a[0])),
            ["acos"] = (1, 1, a => Math.Acos(a[0])),
            ["atan"] = (1, 1, a => Math.Atan(a[0])),
            ["atan2"] = (2, 2, a => Math.Atan2(a[0], a[1])),
            ["sinh"] = (1, 1, a => Math.Sinh(a[0])),
            ["cosh"] = (1, 1, a => Math.Cosh(a[0])),
            ["tanh"] = (1, 1, a => Math.Tanh(a[0])),
            ["degrees"] = (1, 1, a => a[0] * 180.0 / Math.PI),
            ["radians"] = (1, 1, a => a[0] * Math.PI / 180.0),
            ["floor"] = (1, 1, a => Math.Floor(a[0])),
            ["ceil"] = (1, 1, a => Math.Ceiling(a[0])),
            ["trunc"] = (1, 1, a => Math.Truncate(a[0])),
            ["factorial"] = (1, 1, a => Factorial(a[0])),
            ["gcd"] = (2, 2, a => Gcd(RequireInteger(a[0], "gcd"), RequireInteger(a[1], "gcd"))),
            ["lcm"] = (2, 2, a => Lcm(RequireInteger(a[0], "lcm"), RequireInteger(a[1], "lcm"))),
        };

    /// <summary>
    /// Evaluate <paramref name="expression"/>.
    /// </summary>
    /// <exception cref="ExpressionException">The expression is malformed or uses a disallowed name.</exception>
    /// <exception cref="DivideByZeroException">A division or modulo by zero was attempted.</exception>
    public static double Evaluate(string expression)
    {
        var tokens = Tokenize(expression ?? string.Empty);
        var index = 0;
        var value = ParseExpression(tokens, ref index);
        if (index != tokens.Count)
        {
            throw new ExpressionException($"unexpected '{tokens[index].Text}'");
        }
        return value;
    }

    // ---------------------------------------------------------------------
    // Tokenizer
    // ---------------------------------------------------------------------

    private enum TokenKind
    {
        Number,
        Identifier,
        Operator,
        OpenParen,
        CloseParen,
        Comma,
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Value);

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < input.Length && char.IsAsciiDigit(input[i + 1])))
            {
                var start = i;
                while (i < input.Length && (char.IsAsciiDigit(input[i]) || input[i] == '.' || input[i] == '_'))
                {
                    i++;
                }
                // Exponent notation: 1e6, 2.5E-3.
                if (i < input.Length && (input[i] == 'e' || input[i] == 'E'))
                {
                    var save = i;
                    i++;
                    if (i < input.Length && (input[i] == '+' || input[i] == '-'))
                    {
                        i++;
                    }
                    if (i < input.Length && char.IsAsciiDigit(input[i]))
                    {
                        while (i < input.Length && char.IsAsciiDigit(input[i]))
                        {
                            i++;
                        }
                    }
                    else
                    {
                        i = save;
                    }
                }
                // Python allows digit grouping underscores (1_000); strip them before parsing.
                var text = input[start..i].Replace("_", string.Empty, StringComparison.Ordinal);
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    throw new ExpressionException($"invalid number '{text}'");
                }
                tokens.Add(new Token(TokenKind.Number, text, number));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                {
                    i++;
                }
                tokens.Add(new Token(TokenKind.Identifier, input[start..i], 0));
                continue;
            }

            switch (c)
            {
                case '(':
                    tokens.Add(new Token(TokenKind.OpenParen, "(", 0));
                    i++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.CloseParen, ")", 0));
                    i++;
                    continue;
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, ",", 0));
                    i++;
                    continue;
                case '*' when i + 1 < input.Length && input[i + 1] == '*':
                    tokens.Add(new Token(TokenKind.Operator, "**", 0));
                    i += 2;
                    continue;
                case '/' when i + 1 < input.Length && input[i + 1] == '/':
                    tokens.Add(new Token(TokenKind.Operator, "//", 0));
                    i += 2;
                    continue;
                case '+' or '-' or '*' or '/' or '%':
                    tokens.Add(new Token(TokenKind.Operator, c.ToString(), 0));
                    i++;
                    continue;
                default:
                    throw new ExpressionException($"unsupported character '{c}'");
            }
        }
        return tokens;
    }

    // ---------------------------------------------------------------------
    // Recursive-descent parser
    // ---------------------------------------------------------------------

    private static double ParseExpression(List<Token> tokens, ref int index)
    {
        var left = ParseTerm(tokens, ref index);
        while (TryPeekOperator(tokens, index, out var op) && (op == "+" || op == "-"))
        {
            index++;
            var right = ParseTerm(tokens, ref index);
            left = op == "+" ? left + right : left - right;
        }
        return left;
    }

    private static double ParseTerm(List<Token> tokens, ref int index)
    {
        var left = ParseUnary(tokens, ref index);
        while (TryPeekOperator(tokens, index, out var op)
            && (op == "*" || op == "/" || op == "%" || op == "//"))
        {
            index++;
            var right = ParseUnary(tokens, ref index);
            left = op switch
            {
                "*" => left * right,
                "/" => Divide(left, right),
                "//" => FloorDivide(left, right),
                _ => Modulo(left, right),
            };
        }
        return left;
    }

    private static double ParseUnary(List<Token> tokens, ref int index)
    {
        if (TryPeekOperator(tokens, index, out var op) && (op == "+" || op == "-"))
        {
            index++;
            var operand = ParseUnary(tokens, ref index);
            return op == "-" ? -operand : operand;
        }
        return ParsePower(tokens, ref index);
    }

    private static double ParsePower(List<Token> tokens, ref int index)
    {
        var left = ParsePrimary(tokens, ref index);
        if (TryPeekOperator(tokens, index, out var op) && op == "**")
        {
            index++;
            // Right operand parsed as a unary expression: right-associative, and 2**-1 is legal.
            var right = ParseUnary(tokens, ref index);
            return Math.Pow(left, right);
        }
        return left;
    }

    private static double ParsePrimary(List<Token> tokens, ref int index)
    {
        if (index >= tokens.Count)
        {
            throw new ExpressionException("unexpected end of expression");
        }

        var token = tokens[index];
        switch (token.Kind)
        {
            case TokenKind.Number:
                index++;
                return token.Value;

            case TokenKind.OpenParen:
            {
                index++;
                var inner = ParseExpression(tokens, ref index);
                Expect(tokens, ref index, TokenKind.CloseParen, ")");
                return inner;
            }

            case TokenKind.Identifier:
            {
                index++;
                var name = token.Text;
                var isCall = index < tokens.Count && tokens[index].Kind == TokenKind.OpenParen;

                if (!isCall)
                {
                    if (Constants.TryGetValue(name, out var constant))
                    {
                        return constant;
                    }
                    if (Functions.ContainsKey(name))
                    {
                        throw new ExpressionException($"'{name}' is a function, not a value");
                    }
                    throw new ExpressionException($"unknown name '{name}'");
                }

                if (!Functions.TryGetValue(name, out var function))
                {
                    throw new ExpressionException($"unknown function '{name}'");
                }

                index++; // consume '('
                var args = new List<double>();
                if (index < tokens.Count && tokens[index].Kind != TokenKind.CloseParen)
                {
                    args.Add(ParseExpression(tokens, ref index));
                    while (index < tokens.Count && tokens[index].Kind == TokenKind.Comma)
                    {
                        index++;
                        args.Add(ParseExpression(tokens, ref index));
                    }
                }
                Expect(tokens, ref index, TokenKind.CloseParen, ")");

                if (args.Count < function.MinArgs || args.Count > function.MaxArgs)
                {
                    var expected = function.MinArgs == function.MaxArgs
                        ? function.MinArgs.ToString(CultureInfo.InvariantCulture)
                        : $"{function.MinArgs} to {function.MaxArgs}";
                    throw new ExpressionException(
                        $"{name}() takes {expected} argument(s) but {args.Count} were given");
                }
                return function.Invoke([.. args]);
            }

            default:
                throw new ExpressionException($"unexpected '{token.Text}'");
        }
    }

    private static bool TryPeekOperator(List<Token> tokens, int index, out string op)
    {
        if (index < tokens.Count && tokens[index].Kind == TokenKind.Operator)
        {
            op = tokens[index].Text;
            return true;
        }
        op = string.Empty;
        return false;
    }

    private static void Expect(List<Token> tokens, ref int index, TokenKind kind, string text)
    {
        if (index >= tokens.Count || tokens[index].Kind != kind)
        {
            throw new ExpressionException($"expected '{text}'");
        }
        index++;
    }

    // ---------------------------------------------------------------------
    // Python-compatible arithmetic helpers
    // ---------------------------------------------------------------------

    private static double Divide(double left, double right) =>
        right == 0 ? throw new DivideByZeroException() : left / right;

    private static double FloorDivide(double left, double right) =>
        right == 0 ? throw new DivideByZeroException() : Math.Floor(left / right);

    /// <summary>Python's <c>%</c>: the result takes the sign of the divisor, unlike .NET's.</summary>
    private static double Modulo(double left, double right)
    {
        if (right == 0)
        {
            throw new DivideByZeroException();
        }
        var remainder = left % right;
        return remainder != 0 && (remainder < 0) != (right < 0) ? remainder + right : remainder;
    }

    private static double Factorial(double value)
    {
        var n = RequireInteger(value, "factorial");
        if (n < 0)
        {
            throw new ExpressionException("factorial() not defined for negative values");
        }
        // 171! overflows double. Python has arbitrary-precision integers and would keep going;
        // reporting the limit is more useful than returning Infinity.
        if (n > 170)
        {
            throw new ExpressionException("factorial() argument too large (maximum is 170)");
        }
        var result = 1.0;
        for (var i = 2L; i <= n; i++)
        {
            result *= i;
        }
        return result;
    }

    private static double Gcd(long a, long b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a;
    }

    private static double Lcm(long a, long b)
    {
        if (a == 0 || b == 0)
        {
            return 0;
        }
        return Math.Abs(a / (long)Gcd(a, b) * b);
    }

    private static long RequireInteger(double value, string function)
    {
        if (!double.IsFinite(value) || value != Math.Floor(value) || Math.Abs(value) > long.MaxValue)
        {
            throw new ExpressionException($"{function}() requires an integer argument");
        }
        return (long)value;
    }

    private static int RequireDigits(double value)
    {
        var digits = RequireInteger(value, "round");
        if (digits is < 0 or > 15)
        {
            throw new ExpressionException("round() digits must be between 0 and 15");
        }
        return (int)digits;
    }

    private static double RequireNonNegative(double value, string function) =>
        value < 0 ? throw new ExpressionException($"{function}() of a negative number is undefined") : value;
}
