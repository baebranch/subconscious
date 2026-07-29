using FluentAssertions;
using Subconscious.Engine.Tools;
using Subconscious.Engine.Tools.Builtin;

namespace Subconscious.Engine.Tests.Tools;

public class CalculatorToolModuleTests
{
    [Fact]
    public void CreateTools_ExposesPythonToolNames()
    {
        var tools = new CalculatorToolModule().CreateTools(EngineContext.ForCatalog);

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            ["calculate", "convert_units", "list_supported_units"]);
    }

    [Theory]
    [InlineData("2 + 2", "4")]
    [InlineData("sqrt(144)", "12")]          // integral result prints without ".0"
    [InlineData("factorial(6)", "720")]
    [InlineData("7 / 2", "3.5")]
    public void Calculate_FormatsResultsLikeThePythonTool(string expression, string expected)
    {
        CalculatorToolModule.Calculate(expression).Should().Be(expected);
    }

    [Fact]
    public void Calculate_DivisionByZero_ReturnsFriendlyError()
    {
        CalculatorToolModule.Calculate("1 / 0").Should().Be("Error: division by zero.");
    }

    [Fact]
    public void Calculate_MalformedExpression_ReturnsErrorStringNotException()
    {
        // Tools return error strings so the model can relay them; they must not throw.
        CalculatorToolModule.Calculate("2 +").Should().StartWith("Error evaluating expression:");
    }

    [Fact]
    public void ConvertUnits_LengthConversion_RoundsToSixDecimals()
    {
        CalculatorToolModule.ConvertUnits(100, "km", "mi").Should().Be("100.0 km = 62.137119 mi");
    }

    [Fact]
    public void ConvertUnits_DataConversion_UsesBinaryMultiples()
    {
        CalculatorToolModule.ConvertUnits(5, "gb", "mb").Should().Be("5.0 gb = 5120.0 mb");
    }

    [Theory]
    [InlineData(32, "f", "c", "32.0°F = 0.0°C")]
    [InlineData(100, "c", "f", "100.0°C = 212.0°F")]
    [InlineData(0, "c", "k", "0.0°C = 273.15°K")]
    public void ConvertUnits_Temperature_UsesAffineConversion(
        double value, string from, string to, string expected)
    {
        CalculatorToolModule.ConvertUnits(value, from, to).Should().Be(expected);
    }

    [Fact]
    public void ConvertUnits_MismatchedCategories_Explains()
    {
        CalculatorToolModule.ConvertUnits(1, "km", "kg")
            .Should().Contain("different categories");
    }

    [Theory]
    [InlineData("furlong", "km")]
    [InlineData("km", "furlong")]
    public void ConvertUnits_UnknownUnit_ReportsIt(string from, string to)
    {
        CalculatorToolModule.ConvertUnits(1, from, to).Should().StartWith("Unknown unit");
    }

    [Fact]
    public void ConvertUnits_IsCaseAndWhitespaceInsensitive()
    {
        CalculatorToolModule.ConvertUnits(1, " KM ", "M").Should().Be("1.0  KM  = 1000.0 M");
    }

    [Fact]
    public void ListSupportedUnits_GroupsByCategoryIncludingTemperature()
    {
        var groups = CalculatorToolModule.ListSupportedUnits();

        groups.Keys.Should().Contain(["length", "mass", "area", "volume", "speed", "data", "temperature"]);
        groups["temperature"].Should().BeEquivalentTo(["c", "f", "k"]);
        groups["length"].Should().Contain("km");
    }
}
