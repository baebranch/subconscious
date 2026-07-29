using System.Text.RegularExpressions;
using FluentAssertions;
using Subconscious.Engine.Tools;
using Subconscious.Engine.Tools.Builtin;

namespace Subconscious.Engine.Tests.Tools;

public class TimeToolModuleTests
{
    [Fact]
    public void CreateTools_ExposesPythonToolNames()
    {
        var tools = new TimeToolModule().CreateTools(EngineContext.ForCatalog);

        tools.Select(t => t.Name).Should().BeEquivalentTo(
            ["get_current_time", "get_current_date", "convert_timezone", "list_common_timezones"]);
    }

    [Fact]
    public void GetCurrentTime_DefaultsToUtc()
    {
        var result = TimeToolModule.GetCurrentTime();

        result.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC \(UTC\+0000\)$");
    }

    [Fact]
    public void GetCurrentTime_IanaZone_ResolvesOnThisPlatform()
    {
        // .NET 6+ accepts IANA ids on Windows too, so the Python tool contract is unchanged.
        var result = TimeToolModule.GetCurrentTime("Asia/Kolkata");

        result.Should().Contain("Asia/Kolkata").And.Contain("(UTC+0530)");
    }

    [Fact]
    public void GetCurrentTime_UnknownZone_ReturnsGuidance()
    {
        TimeToolModule.GetCurrentTime("Mars/Olympus_Mons")
            .Should().StartWith("Unknown timezone 'Mars/Olympus_Mons'.")
            .And.Contain("IANA");
    }

    [Fact]
    public void GetCurrentDate_ReturnsLongInvariantFormat()
    {
        TimeToolModule.GetCurrentDate()
            .Should().MatchRegex(@"^[A-Z][a-z]+day, \d{2} [A-Z][a-z]+ \d{4}$");
    }

    [Fact]
    public void GetCurrentDate_UnknownZone_ReturnsError()
    {
        TimeToolModule.GetCurrentDate("Nowhere/Here").Should().Be("Unknown timezone 'Nowhere/Here'.");
    }

    [Fact]
    public void ConvertTimezone_FullDateTime_ConvertsAcrossZones()
    {
        // 2026-03-16 14:30 New York (EDT, UTC-4) is 2026-03-17 03:30 Tokyo (UTC+9).
        var result = TimeToolModule.ConvertTimezone("2026-03-16 14:30", "America/New_York", "Asia/Tokyo");

        result.Should().Be("2026-03-17 03:30 Asia/Tokyo (UTC+0900)");
    }

    [Fact]
    public void ConvertTimezone_TimeOnly_UsesTodaysDate()
    {
        // Deliberate divergence from Python's strptime default of 1900-01-01, which resolves DST
        // with century-old rules. "14:30" should mean today at 14:30.
        var result = TimeToolModule.ConvertTimezone("14:30", "UTC", "UTC");

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        result.Should().Be($"{today} 14:30 UTC (UTC+0000)");
    }

    [Theory]
    [InlineData("half past two")]
    [InlineData("2:30 PM")]
    [InlineData("2026-03-16")]
    public void ConvertTimezone_UnparseableTime_ExplainsAcceptedFormats(string timeStr)
    {
        TimeToolModule.ConvertTimezone(timeStr, "UTC", "UTC")
            .Should().Contain("Use 'HH:MM' or 'YYYY-MM-DD HH:MM'");
    }

    [Theory]
    [InlineData("Nowhere/Here", "UTC")]
    [InlineData("UTC", "Nowhere/Here")]
    public void ConvertTimezone_UnknownZone_ReportsIt(string from, string to)
    {
        TimeToolModule.ConvertTimezone("14:30", from, to).Should().StartWith("Unknown timezone:");
    }

    [Fact]
    public void ConvertTimezone_TimeInDaylightSavingGap_ExplainsInsteadOfThrowing()
    {
        // 2026-03-08 02:30 does not exist in New York: clocks jump 02:00 to 03:00.
        var result = TimeToolModule.ConvertTimezone("2026-03-08 02:30", "America/New_York", "UTC");

        result.Should().Contain("does not exist").And.Contain("daylight-saving gap");
    }

    [Fact]
    public void ListCommonTimezones_EntriesAreAllResolvable()
    {
        var zones = TimeToolModule.ListCommonTimezones();

        zones.Should().Contain("UTC").And.HaveCountGreaterThan(10);
        foreach (var zone in zones)
        {
            // A reference list the model is told to trust must not contain unusable ids.
            TimeToolModule.GetCurrentTime(zone).Should().NotStartWith("Unknown timezone");
        }
    }

    [Fact]
    public void ToolDescriptions_AreSingleLineSummaries()
    {
        var tools = new TimeToolModule().CreateTools(EngineContext.ForCatalog);

        foreach (var tool in tools)
        {
            tool.Description.Should().NotBeNullOrWhiteSpace();
            Regex.IsMatch(tool.Description, "\n").Should().BeFalse();
        }
    }
}
