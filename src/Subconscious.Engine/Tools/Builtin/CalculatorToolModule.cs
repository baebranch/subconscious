using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace Subconscious.Engine.Tools.Builtin;

/// <summary>
/// Expression evaluation and unit conversion. Port of <c>tools/calculator.py</c>. Expression
/// evaluation is delegated to <see cref="SafeExpressionEvaluator"/>, which preserves the Python
/// version's core property: no code execution path exists, only a whitelisted grammar.
///
/// <para>
/// Like the Python original, these tools return human-readable strings (and error strings rather
/// than raised exceptions) so a model can relay the result directly.
/// </para>
/// </summary>
public sealed class CalculatorToolModule : IToolModule
{
    public string Slug => "calculator";

    /// <summary>Unit abbreviation to its category and factor relative to that category's base unit.</summary>
    private static readonly Dictionary<string, (string Category, double Factor)> UnitTable =
        new(StringComparer.Ordinal)
        {
            // length (base: metre)
            ["mm"] = ("length", 0.001), ["cm"] = ("length", 0.01), ["m"] = ("length", 1.0),
            ["km"] = ("length", 1000.0), ["in"] = ("length", 0.0254), ["ft"] = ("length", 0.3048),
            ["yd"] = ("length", 0.9144), ["mi"] = ("length", 1609.344), ["nmi"] = ("length", 1852.0),
            // mass (base: kilogram)
            ["mg"] = ("mass", 1e-6), ["g"] = ("mass", 0.001), ["kg"] = ("mass", 1.0),
            ["t"] = ("mass", 1000.0), ["oz"] = ("mass", 0.02835), ["lb"] = ("mass", 0.45359),
            ["st"] = ("mass", 6.35029),
            // temperature is handled separately (affine, not multiplicative)
            // area (base: square metre)
            ["mm2"] = ("area", 1e-6), ["cm2"] = ("area", 1e-4), ["m2"] = ("area", 1.0),
            ["km2"] = ("area", 1e6), ["ft2"] = ("area", 0.0929), ["ac"] = ("area", 4046.86),
            // volume (base: litre)
            ["ml"] = ("volume", 0.001), ["l"] = ("volume", 1.0), ["dl"] = ("volume", 0.1),
            ["m3"] = ("volume", 1000.0), ["fl_oz"] = ("volume", 0.02957), ["pt"] = ("volume", 0.47318),
            ["qt"] = ("volume", 0.94635), ["gal"] = ("volume", 3.78541),
            // speed (base: metre/second)
            ["m/s"] = ("speed", 1.0), ["km/h"] = ("speed", 1.0 / 3.6), ["mph"] = ("speed", 0.44704),
            ["knot"] = ("speed", 0.51444),
            // data (base: byte, binary multiples as in the Python table)
            ["b"] = ("data", 1), ["kb"] = ("data", 1024), ["mb"] = ("data", 1024.0 * 1024),
            ["gb"] = ("data", 1024.0 * 1024 * 1024), ["tb"] = ("data", 1024.0 * 1024 * 1024 * 1024),
        };

    private static readonly string[] TemperatureUnits = ["c", "f", "k"];

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return
        [
            AIFunctionFactory.Create(
                Calculate,
                "calculate",
                "Evaluate a mathematical expression and return the result. Supports + - * / ** % //, "
                + "and functions like sqrt(), sin(), log(), factorial(). Constants pi, e, tau, inf are "
                + "also available. Examples: '2 + 2', 'sqrt(144)', 'sin(radians(30))', 'log(1000, 10)'."),
            AIFunctionFactory.Create(
                ConvertUnits,
                "convert_units",
                "Convert a numeric value from one unit to another. Supported categories: length, mass, "
                + "area, volume, speed, data. Temperature uses special handling (c, f, k)."),
            AIFunctionFactory.Create(
                ListSupportedUnits,
                "list_supported_units",
                "Return all supported unit abbreviations grouped by category, including temperature "
                + "units c (Celsius), f (Fahrenheit) and k (Kelvin)."),
        ];
    }

    internal static string Calculate(
        [Description("The mathematical expression to evaluate, e.g. 'sqrt(144) * 2'.")] string expression)
    {
        try
        {
            return FormatNumber(SafeExpressionEvaluator.Evaluate(expression));
        }
        catch (DivideByZeroException)
        {
            return "Error: division by zero.";
        }
        catch (ExpressionException exc)
        {
            return $"Error evaluating expression: {exc.Message}";
        }
    }

    internal static string ConvertUnits(
        [Description("The numeric value to convert.")] double value,
        [Description("Source unit abbreviation, e.g. 'km', 'lb', 'gb', 'f'.")] string fromUnit,
        [Description("Target unit abbreviation, e.g. 'mi', 'kg', 'mb', 'c'.")] string toUnit)
    {
        var from = (fromUnit ?? string.Empty).ToLowerInvariant().Trim();
        var to = (toUnit ?? string.Empty).ToLowerInvariant().Trim();

        if (TemperatureUnits.Contains(from) || TemperatureUnits.Contains(to))
        {
            return ConvertTemperature(value, from, to);
        }

        if (!UnitTable.TryGetValue(from, out var source))
        {
            return $"Unknown unit '{fromUnit}'.";
        }
        if (!UnitTable.TryGetValue(to, out var target))
        {
            return $"Unknown unit '{toUnit}'.";
        }
        if (source.Category != target.Category)
        {
            return $"Cannot convert {fromUnit} ({source.Category}) to {toUnit} ({target.Category}) "
                + "— different categories.";
        }

        var result = value * source.Factor / target.Factor;
        return $"{FormatFloat(value)} {fromUnit} = {FormatFloat(Math.Round(result, 6))} {toUnit}";
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ListSupportedUnits()
    {
        var groups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (unit, (category, _)) in UnitTable)
        {
            if (!groups.TryGetValue(category, out var list))
            {
                list = new List<string>();
                groups[category] = list;
            }
            ((List<string>)list).Add(unit);
        }
        groups["temperature"] = TemperatureUnits;
        return groups;
    }

    private static string ConvertTemperature(double value, string from, string to)
    {
        // Normalize through Celsius, as the Python implementation does.
        double celsius;
        switch (from)
        {
            case "c": celsius = value; break;
            case "f": celsius = (value - 32) * 5 / 9; break;
            case "k": celsius = value - 273.15; break;
            default: return $"Unknown temperature unit '{from}'.";
        }

        double result;
        switch (to)
        {
            case "c": result = celsius; break;
            case "f": result = celsius * 9 / 5 + 32; break;
            case "k": result = celsius + 273.15; break;
            default: return $"Unknown temperature unit '{to}'.";
        }

        return $"{FormatFloat(value)}°{from.ToUpperInvariant()} = "
            + $"{FormatFloat(Math.Round(result, 4))}°{to.ToUpperInvariant()}";
    }

    /// <summary>
    /// Format a <see cref="Calculate"/> result the way the Python tool did: an integral value is
    /// rendered without a decimal point (so "2 + 2" gives "4", not "4.0"), anything else uses the
    /// shortest round-trippable representation.
    /// </summary>
    private static string FormatNumber(double value)
    {
        if (double.IsFinite(value) && value == Math.Floor(value) && Math.Abs(value) < 1e15)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }
        return FormatDouble(value);
    }

    /// <summary>
    /// Format a value the way Python renders a <c>float</c>, i.e. keeping a trailing ".0" for
    /// integral values. Used by the conversion tools, whose inputs and outputs are always floats
    /// in the Python version ("100.0 km = 62.137119 mi").
    /// </summary>
    private static string FormatFloat(double value)
    {
        if (double.IsFinite(value) && value == Math.Floor(value) && Math.Abs(value) < 1e16)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture) + ".0";
        }
        return FormatDouble(value);
    }

    private static string FormatDouble(double value) => value switch
    {
        double.PositiveInfinity => "inf",
        double.NegativeInfinity => "-inf",
        double.NaN => "nan",
        _ => value.ToString("R", CultureInfo.InvariantCulture),
    };
}
