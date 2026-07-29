using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace Subconscious.Engine.Tools.Builtin;

/// <summary>
/// Clock, date and timezone-conversion tools. Port of <c>tools/time_tools.py</c>. No external
/// dependencies beyond the base class library.
///
/// <para>
/// <b>Timezone identifiers.</b> The Python tools took IANA names via <c>zoneinfo</c>, and that
/// contract is preserved: the model keeps passing "Europe/London". See <see cref="TryFindZone"/>
/// for how the lookup is made to work on Windows, whose OS timezone database is keyed by Windows
/// identifiers rather than IANA names.
/// </para>
///
/// <para>
/// <b>Two deliberate output divergences</b> from the Python version, both because the .NET
/// equivalent is either absent or worse:
/// <list type="number">
/// <item><c>strftime</c>'s <c>%Z</c> abbreviation ("PST") has no cross-platform .NET equivalent —
/// <see cref="TimeZoneInfo.StandardName"/> is a localized long name that differs between Windows
/// and Linux, and <see cref="TimeZoneInfo.Id"/> is the Windows identifier on Windows. The zone
/// identifier the caller supplied is echoed instead: stable across platforms and unambiguous.</item>
/// <item>A bare "HH:mm" input to <see cref="ConvertTimezone"/> is interpreted as <em>today</em> at
/// that time. Python's <c>strptime</c> defaulted the date to 1900-01-01, which resolves DST using
/// century-old rules (often a local-mean-time offset with odd minutes) — a latent bug, not
/// behaviour worth reproducing.</item>
/// </list>
/// </para>
/// </summary>
public sealed class TimeToolModule : IToolModule
{
    public string Slug => "time";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The context is unused by time tools (they have no DB or dispatch dependency), matching
        // the Python functions, which accepted RunContext[EngineContext] and ignored it.
        return
        [
            AIFunctionFactory.Create(
                GetCurrentTime,
                "get_current_time",
                "Return the current time in the given IANA timezone (e.g. 'America/New_York'). "
                + "Defaults to UTC. Returns a formatted string including the timezone and UTC offset."),
            AIFunctionFactory.Create(
                GetCurrentDate,
                "get_current_date",
                "Return today's date (weekday, day, month, year) in the given IANA timezone. Defaults to UTC."),
            AIFunctionFactory.Create(
                ConvertTimezone,
                "convert_timezone",
                "Convert a time expressed as 'HH:MM' or 'YYYY-MM-DD HH:MM' from one IANA timezone "
                + "to another. Returns the converted time as a human-readable string."),
            AIFunctionFactory.Create(
                ListCommonTimezones,
                "list_common_timezones",
                "Return a list of commonly used IANA timezone names for reference."),
        ];
    }

    internal static string GetCurrentTime(
        [Description("IANA timezone name, e.g. 'Europe/London'. Defaults to UTC.")] string tz = "UTC")
    {
        if (!TryFindZone(tz, out var zone))
        {
            return $"Unknown timezone '{tz}'. Use an IANA name like 'Europe/London' or 'America/Chicago'.";
        }
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        return $"{now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} "
            + $"{tz.Trim()} (UTC{FormatOffset(now.Offset)})";
    }

    internal static string GetCurrentDate(
        [Description("IANA timezone name, e.g. 'Europe/London'. Defaults to UTC.")] string tz = "UTC")
    {
        if (!TryFindZone(tz, out var zone))
        {
            return $"Unknown timezone '{tz}'.";
        }
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        return today.ToString("dddd, dd MMMM yyyy", CultureInfo.InvariantCulture);
    }

    internal static string ConvertTimezone(
        [Description("Time to convert, e.g. '14:30' or '2026-03-16 14:30'.")] string timeStr,
        [Description("Source IANA timezone, e.g. 'America/New_York'.")] string fromTz,
        [Description("Target IANA timezone, e.g. 'Asia/Tokyo'.")] string toTz)
    {
        string[] formats = ["yyyy-MM-dd HH:mm", "HH:mm"];
        if (!DateTime.TryParseExact(
                (timeStr ?? string.Empty).Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var naive))
        {
            return $"Could not parse '{timeStr}'. Use 'HH:MM' or 'YYYY-MM-DD HH:MM'.";
        }

        if (!TryFindZone(fromTz, out var sourceZone))
        {
            return $"Unknown timezone: {fromTz}";
        }
        if (!TryFindZone(toTz, out var targetZone))
        {
            return $"Unknown timezone: {toTz}";
        }

        naive = DateTime.SpecifyKind(naive, DateTimeKind.Unspecified);
        // An invalid local time (inside a spring-forward gap) has no UTC equivalent; ConvertTime
        // throws for it, so report it instead of surfacing an exception to the model.
        if (sourceZone.IsInvalidTime(naive))
        {
            return $"'{timeStr}' does not exist in {fromTz.Trim()} (it falls in a daylight-saving gap).";
        }

        var converted = TimeZoneInfo.ConvertTime(naive, sourceZone, targetZone);
        var offset = targetZone.GetUtcOffset(converted);
        return $"{converted.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} "
            + $"{toTz.Trim()} (UTC{FormatOffset(offset)})";
    }

    internal static IReadOnlyList<string> ListCommonTimezones() =>
    [
        "UTC",
        "America/New_York", "America/Chicago", "America/Denver",
        "America/Los_Angeles", "America/Toronto", "America/Sao_Paulo",
        "Europe/London", "Europe/Paris", "Europe/Berlin", "Europe/Moscow",
        "Africa/Johannesburg", "Asia/Dubai", "Asia/Kolkata", "Asia/Singapore",
        "Asia/Tokyo", "Asia/Shanghai", "Australia/Sydney", "Pacific/Auckland",
    ];

    /// <summary>
    /// Resolve a timezone identifier, accepting IANA names on every platform.
    ///
    /// <para>
    /// <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> looks up the host OS timezone database,
    /// which on Windows is keyed by Windows identifiers ("India Standard Time"), so an IANA name
    /// is not found directly — verified on this machine, where "Asia/Kolkata" failed outright.
    /// The ICU-backed <see cref="TimeZoneInfo.TryConvertIanaIdToWindowsId"/> bridges that gap.
    /// The reverse conversion is also attempted so a Windows identifier still works on Linux and
    /// macOS, making the tool accept either vocabulary anywhere.
    /// </para>
    /// </summary>
    private static bool TryFindZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var trimmed = id.Trim();
        if (TryLookupZone(trimmed, out zone))
        {
            return true;
        }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(trimmed, out var windowsId)
            && TryLookupZone(windowsId, out zone))
        {
            return true;
        }
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(trimmed, out var ianaId)
            && TryLookupZone(ianaId, out zone))
        {
            return true;
        }
        return false;
    }

    private static bool TryLookupZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            // Corrupt tz database entry: treat as unknown rather than failing the whole turn.
            return false;
        }
    }

    /// <summary>Format a UTC offset the way <c>strftime</c>'s <c>%z</c> does, e.g. "+0530".</summary>
    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{abs.Hours:00}{abs.Minutes:00}");
    }
}
