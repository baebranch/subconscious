using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using Subconscious.Engine.Tools.Builtin;

namespace Subconscious.Engine.Tests.Tools;

public class WeatherToolModuleTests
{
    [Fact]
    public void Slug_ReturnsWeather()
    {
        var module = new WeatherToolModule();
        Assert.Equal("weather", module.Slug);
    }

    [Fact]
    public void CreateTools_ReturnsExpectedTools()
    {
        var module = new WeatherToolModule();
        var context = EngineContext.ForCatalog;

        var tools = module.CreateTools(context);

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "get_current_weather");
        Assert.Contains(tools, t => t.Name == "get_weather_forecast");
    }

    [Fact]
    public void GetWeather_HasCorrectDescription()
    {
        var module = new WeatherToolModule();
        var context = EngineContext.ForCatalog;
        var tools = module.CreateTools(context);
        var getWeatherTool = tools.First(t => t.Name == "get_current_weather");

        Assert.Contains("current weather", getWeatherTool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetForecast_HasCorrectDescription()
    {
        var module = new WeatherToolModule();
        var context = EngineContext.ForCatalog;
        var tools = module.CreateTools(context);
        var getForecastTool = tools.First(t => t.Name == "get_weather_forecast");

        Assert.Contains("forecast", getForecastTool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWeather_ReturnsWeatherData_ForValidLocation()
    {
        var module = new WeatherToolModule();
        var context = EngineContext.ForCatalog;
        var tools = module.CreateTools(context);
        var getWeatherTool = tools.First(t => t.Name == "get_current_weather");

        // Invoke the tool with London as the test location
        var args = new AIFunctionArguments
        {
            ["location"] = "London"
        };
        var result = await getWeatherTool.InvokeAsync(args);

        Assert.NotNull(result);
        var resultStr = result.ToString();
        Assert.NotNull(resultStr);
        
        // Should contain basic weather information
        Assert.Contains("Weather in London", resultStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Temperature", resultStr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetForecast_ReturnsMultiDayForecast_ForValidLocation()
    {
        var module = new WeatherToolModule();
        var context = EngineContext.ForCatalog;
        var tools = module.CreateTools(context);
        var getForecastTool = tools.First(t => t.Name == "get_weather_forecast");

        // Invoke the tool with Tokyo as the test location
        var args = new AIFunctionArguments
        {
            ["location"] = "Tokyo",
            ["days"] = 2
        };
        var result = await getForecastTool.InvokeAsync(args);

        Assert.NotNull(result);
        var resultStr = result.ToString();
        Assert.NotNull(resultStr);
        
        // Should contain forecast information
        Assert.Contains("forecast for Tokyo", resultStr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Day 1", resultStr);
        Assert.Contains("Day 2", resultStr);
    }

    [Fact]
    public async Task GetWeather_HandlesInvalidLocation()
    {
        var module = new WeatherToolModule();
        var context = EngineContext.ForCatalog;
        var tools = module.CreateTools(context);
        var getWeatherTool = tools.First(t => t.Name == "get_current_weather");

        // Use an obviously invalid location
        var args = new AIFunctionArguments
        {
            ["location"] = "InvalidCityNameThatDoesNotExist123456"
        };
        var result = await getWeatherTool.InvokeAsync(args);

        Assert.NotNull(result);
        var resultStr = result.ToString();
        Assert.NotNull(resultStr);
        
        // Should return some kind of error or "could not retrieve" message
        Assert.True(
            resultStr.Contains("Could not retrieve", StringComparison.OrdinalIgnoreCase) ||
            resultStr.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            resultStr.Contains("Failed", StringComparison.OrdinalIgnoreCase),
            "Expected error message for invalid location");
    }

    [Fact]
    public async Task GetForecast_ClampsOutOfRangeDays()
    {
        var module = new WeatherToolModule();
        var context = EngineContext.ForCatalog;
        var tools = module.CreateTools(context);
        var getForecastTool = tools.First(t => t.Name == "get_weather_forecast");

        // Try with days > 3 (should clamp to 3)
        var args = new AIFunctionArguments
        {
            ["location"] = "Paris",
            ["days"] = 10
        };
        var result = await getForecastTool.InvokeAsync(args);

        Assert.NotNull(result);
        var resultStr = result.ToString();
        Assert.NotNull(resultStr);
        
        // Should still return a valid forecast (clamped to 3 days)
        Assert.Contains("forecast for Paris", resultStr, StringComparison.OrdinalIgnoreCase);
    }
}
