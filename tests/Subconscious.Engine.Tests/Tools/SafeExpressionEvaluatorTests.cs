using FluentAssertions;
using Subconscious.Engine.Tools.Builtin;

namespace Subconscious.Engine.Tests.Tools;

public class SafeExpressionEvaluatorTests
{
    [Theory]
    [InlineData("2 + 2", 4)]
    [InlineData("10 - 3 - 2", 5)]          // left-associative
    [InlineData("2 * 3 + 4", 10)]           // precedence
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("7 / 2", 3.5)]              // true division, as in Python 3
    [InlineData("7 // 2", 3)]
    [InlineData("-7 // 2", -4)]             // floor division rounds toward negative infinity
    [InlineData("2 ** 10", 1024)]
    [InlineData("1_000 + 1", 1001)]         // Python digit-grouping underscores
    [InlineData("1e3", 1000)]
    [InlineData("2.5E-1", 0.25)]
    public void Evaluate_Arithmetic_MatchesPythonSemantics(string expression, double expected)
    {
        SafeExpressionEvaluator.Evaluate(expression).Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData("2 ** 3 ** 2", 512)]        // right-associative: 2**(3**2)
    [InlineData("-2 ** 2", -4)]             // ** binds tighter than unary minus
    [InlineData("2 ** -1", 0.5)]            // unary allowed on the right of **
    public void Evaluate_PowerOperator_FollowsPythonAssociativityAndBinding(string expression, double expected)
    {
        SafeExpressionEvaluator.Evaluate(expression).Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData("7 % 3", 1)]
    [InlineData("-7 % 3", 2)]               // Python modulo takes the sign of the divisor
    [InlineData("7 % -3", -2)]
    public void Evaluate_Modulo_TakesSignOfDivisor(string expression, double expected)
    {
        SafeExpressionEvaluator.Evaluate(expression).Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData("sqrt(144)", 12)]
    [InlineData("abs(-5)", 5)]
    [InlineData("round(2.5)", 2)]           // banker's rounding, as in Python
    [InlineData("round(3.14159, 2)", 3.14)]
    [InlineData("sin(radians(30))", 0.5)]
    [InlineData("log(1000, 10)", 3)]
    [InlineData("log10(1000)", 3)]
    [InlineData("factorial(6)", 720)]
    [InlineData("gcd(12, 18)", 6)]
    [InlineData("lcm(4, 6)", 12)]
    [InlineData("floor(2.9)", 2)]
    [InlineData("ceil(2.1)", 3)]
    [InlineData("trunc(-2.7)", -2)]
    [InlineData("atan2(1, 1) * 4", Math.PI)]
    [InlineData("cbrt(27)", 3)]
    public void Evaluate_WhitelistedFunctions_ReturnExpectedValues(string expression, double expected)
    {
        SafeExpressionEvaluator.Evaluate(expression).Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("pi", Math.PI)]
    [InlineData("e", Math.E)]
    [InlineData("tau", Math.Tau)]
    public void Evaluate_Constants_AreAvailable(string expression, double expected)
    {
        SafeExpressionEvaluator.Evaluate(expression).Should().BeApproximately(expected, 1e-12);
    }

    [Fact]
    public void Evaluate_Inf_ReturnsPositiveInfinity()
    {
        SafeExpressionEvaluator.Evaluate("inf").Should().Be(double.PositiveInfinity);
    }

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("1 // 0")]
    [InlineData("1 % 0")]
    public void Evaluate_DivisionByZero_ThrowsRatherThanReturningInfinity(string expression)
    {
        // .NET floating point would silently yield Infinity here; Python raises ZeroDivisionError
        // and the tool surfaces "Error: division by zero.", so the evaluator must throw.
        var act = () => SafeExpressionEvaluator.Evaluate(expression);
        act.Should().Throw<DivideByZeroException>();
    }

    [Theory]
    [InlineData("__import__('os')")]                 // no code execution surface at all
    [InlineData("open('secrets.txt')")]
    [InlineData("os.system('calc')")]
    [InlineData("1; 2")]
    [InlineData("[1, 2, 3]")]
    [InlineData("'string'")]
    [InlineData("x = 1")]
    public void Evaluate_DisallowedConstructs_Throw(string expression)
    {
        var act = () => SafeExpressionEvaluator.Evaluate(expression);
        act.Should().Throw<ExpressionException>();
    }

    [Fact]
    public void Evaluate_FunctionNameUsedAsValue_ThrowsWithExplanation()
    {
        var act = () => SafeExpressionEvaluator.Evaluate("sqrt + 1");
        act.Should().Throw<ExpressionException>().WithMessage("*is a function, not a value*");
    }

    [Theory]
    [InlineData("sqrt()")]
    [InlineData("sqrt(1, 2)")]
    [InlineData("atan2(1)")]
    public void Evaluate_WrongArity_Throws(string expression)
    {
        var act = () => SafeExpressionEvaluator.Evaluate(expression);
        act.Should().Throw<ExpressionException>().WithMessage("*argument*");
    }

    [Theory]
    [InlineData("2 +")]
    [InlineData("(2 + 3")]
    [InlineData("2 3")]
    [InlineData("")]
    public void Evaluate_MalformedExpression_Throws(string expression)
    {
        var act = () => SafeExpressionEvaluator.Evaluate(expression);
        act.Should().Throw<ExpressionException>();
    }

    [Fact]
    public void Evaluate_FactorialOfNonInteger_Throws()
    {
        var act = () => SafeExpressionEvaluator.Evaluate("factorial(2.5)");
        act.Should().Throw<ExpressionException>().WithMessage("*integer*");
    }

    [Fact]
    public void Evaluate_FactorialBeyondDoubleRange_ThrowsWithLimit()
    {
        // 171! overflows double; reporting the limit beats returning Infinity.
        var act = () => SafeExpressionEvaluator.Evaluate("factorial(171)");
        act.Should().Throw<ExpressionException>().WithMessage("*maximum is 170*");
    }
}
