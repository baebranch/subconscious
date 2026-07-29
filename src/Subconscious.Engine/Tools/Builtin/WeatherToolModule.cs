using Microsoft.Extensions.AI;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Subconscious.Engine.Tools.Builtin;

/// <summary>
/// Weather information tools using the free wttr.in service (no API key required).
/// Port of the Python implementation's weather tools.
/// </summary>
public class WeatherToolModule : IToolModule
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public string Slug => "weather";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                GetWeather,
                "get_current_weather",
                "Get the current weather conditions for a location. No API key required — uses the free wttr.in service."),
            AIFunctionFactory.Create(
                GetForecast,
                "get_weather_forecast",
                "Get a multi-day weather forecast for a location (up to 3 days). No API key required.")
        ];
    }

    /// <summary>
    /// Get the current weather conditions for a location.
    /// No API key required — uses the free wttr.in service.
    /// </summary>
    /// <param name="location">City name or 'City, Country', e.g. 'London', 'Paris, FR', 'Tokyo'.</param>
    private static async Task<string> GetWeather(string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location, nameof(location));

        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(location)}?format=j1";
            var response = await HttpClient.GetFromJsonAsync<WttrResponse>(url);

            if (response?.CurrentCondition?.FirstOrDefault() is not { } current)
            {
                return $"Could not retrieve weather data for '{location}'.";
            }

            var temp = current.TempC ?? "?";
            var feelsLike = current.FeelsLikeC ?? "?";
            var condition = current.WeatherDesc?.FirstOrDefault()?.Value ?? "Unknown";
            var humidity = current.Humidity ?? "?";
            var windSpeed = current.WindspeedKmph ?? "?";
            var windDir = current.WindDir16Point ?? "?";
            var precipitation = current.PrecipMM ?? "?";
            var pressure = current.Pressure ?? "?";
            var visibility = current.VisibilityKM ?? "?";
            var cloudCover = current.CloudCover ?? "?";

            return $"""
                Weather in {location}:
                Temperature: {temp}°C (feels like {feelsLike}°C)
                Condition: {condition}
                Humidity: {humidity}%
                Wind: {windSpeed} km/h {windDir}
                Precipitation: {precipitation} mm
                Pressure: {pressure} mb
                Visibility: {visibility} km
                Cloud cover: {cloudCover}%
                """;
        }
        catch (HttpRequestException ex)
        {
            return $"Failed to fetch weather data: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error retrieving weather: {ex.Message}";
        }
    }

    /// <summary>
    /// Get a multi-day weather forecast for a location (up to 3 days).
    /// No API key required.
    /// </summary>
    /// <param name="location">City name or 'City, Country'.</param>
    /// <param name="days">Number of days to forecast (1–3, default 3).</param>
    private static async Task<string> GetForecast(string location, int days = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location, nameof(location));

        if (days is < 1 or > 3)
        {
            days = 3;
        }

        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(location)}?format=j1";
            var response = await HttpClient.GetFromJsonAsync<WttrResponse>(url);

            if (response?.Weather is null || response.Weather.Count == 0)
            {
                return $"Could not retrieve forecast data for '{location}'.";
            }

            var result = new System.Text.StringBuilder();
            result.AppendLine($"{days}-day forecast for {location}:");
            result.AppendLine();

            for (int i = 0; i < Math.Min(days, response.Weather.Count); i++)
            {
                var day = response.Weather[i];
                var date = day.Date ?? "Unknown date";
                var maxTemp = day.MaxTempC ?? "?";
                var minTemp = day.MinTempC ?? "?";
                var avgTemp = day.AvgTempC ?? "?";
                var condition = day.Hourly?.FirstOrDefault()?.WeatherDesc?.FirstOrDefault()?.Value ?? "Unknown";
                var totalPrecip = day.TotalSnowCM ?? "0";
                var sunHour = day.SunHour ?? "?";
                var uvIndex = day.UvIndex ?? "?";

                result.AppendLine($"Day {i + 1} ({date}):");
                result.AppendLine($"  Temperature: {minTemp}°C to {maxTemp}°C (avg {avgTemp}°C)");
                result.AppendLine($"  Condition: {condition}");
                result.AppendLine($"  Total precipitation: {totalPrecip} mm");
                result.AppendLine($"  Sun hours: {sunHour}");
                result.AppendLine($"  UV Index: {uvIndex}");
                result.AppendLine();
            }

            return result.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Failed to fetch forecast data: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error retrieving forecast: {ex.Message}";
        }
    }

    // JSON response models for wttr.in API
    private sealed class WttrResponse
    {
        [JsonPropertyName("current_condition")]
        public List<CurrentCondition>? CurrentCondition { get; set; }

        [JsonPropertyName("weather")]
        public List<WeatherDay>? Weather { get; set; }
    }

    private sealed class CurrentCondition
    {
        [JsonPropertyName("temp_C")]
        public string? TempC { get; set; }

        [JsonPropertyName("FeelsLikeC")]
        public string? FeelsLikeC { get; set; }

        [JsonPropertyName("weatherDesc")]
        public List<Description>? WeatherDesc { get; set; }

        [JsonPropertyName("humidity")]
        public string? Humidity { get; set; }

        [JsonPropertyName("windspeedKmph")]
        public string? WindspeedKmph { get; set; }

        [JsonPropertyName("winddir16Point")]
        public string? WindDir16Point { get; set; }

        [JsonPropertyName("precipMM")]
        public string? PrecipMM { get; set; }

        [JsonPropertyName("pressure")]
        public string? Pressure { get; set; }

        [JsonPropertyName("visibility")]
        public string? VisibilityKM { get; set; }

        [JsonPropertyName("cloudcover")]
        public string? CloudCover { get; set; }
    }

    private sealed class WeatherDay
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("maxtempC")]
        public string? MaxTempC { get; set; }

        [JsonPropertyName("mintempC")]
        public string? MinTempC { get; set; }

        [JsonPropertyName("avgtempC")]
        public string? AvgTempC { get; set; }

        [JsonPropertyName("totalSnow_cm")]
        public string? TotalSnowCM { get; set; }

        [JsonPropertyName("sunHour")]
        public string? SunHour { get; set; }

        [JsonPropertyName("uvIndex")]
        public string? UvIndex { get; set; }

        [JsonPropertyName("hourly")]
        public List<HourlyCondition>? Hourly { get; set; }
    }

    private sealed class HourlyCondition
    {
        [JsonPropertyName("weatherDesc")]
        public List<Description>? WeatherDesc { get; set; }
    }

    private sealed class Description
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }
}
