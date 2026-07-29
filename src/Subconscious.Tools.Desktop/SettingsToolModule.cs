using System.Collections;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using System.ComponentModel;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// System settings tool module. Provides access to system settings and preferences.
/// Port of Python's <c>desktop_tools/settings.py</c>.
/// </summary>
public sealed class SettingsToolModule : IToolModule
{
    public string Slug => "settings";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                GetSystemInfo,
                "get_system_info",
                "Get system information including OS, CPU, memory, and disk space."),

            AIFunctionFactory.Create(
                GetEnvironmentVariables,
                "get_environment_variables",
                "Get environment variables. Optionally filter by prefix."),

            AIFunctionFactory.Create(
                GetTimezone,
                "get_timezone",
                "Get the current system timezone information.")
        ];
    }

    private static string GetSystemInfo(EngineContext context)
    {
        var osInfo = System.Environment.OSVersion;
        var machineInfo = System.Environment.MachineName;
        var processorCount = System.Environment.ProcessorCount;
        var totalMemory = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;

        var driveInfo = System.IO.DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);
        var diskSpace = driveInfo?.TotalSize / 1024 / 1024 / 1024 ?? 0;

        return $"""
            Operating System: {osInfo.Platform} {osInfo.Version}
            Machine Name: {machineInfo}
            Processor Count: {processorCount}
            Working Set: {totalMemory:N1} MB
            Total Disk Space: {diskSpace:N1} GB
            """;
    }

    private static string GetEnvironmentVariables(
        [Description("Optional prefix to filter environment variables.")] string? prefix = null)
    {
        var variables = System.Environment.GetEnvironmentVariables();
        var output = new System.Text.StringBuilder();

        foreach (DictionaryEntry entry in variables)
        {
            if (string.IsNullOrEmpty(prefix) || entry.Key.ToString()?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                output.AppendLine($"{entry.Key}={entry.Value}");
            }
        }

        return output.ToString().TrimEnd();
    }

    private static string GetTimezone(EngineContext context)
    {
        var timezone = TimeZoneInfo.Local;
        return $"""
            Timezone: {timezone.DisplayName}
            ID: {timezone.Id}
            Base UTC Offset: {timezone.BaseUtcOffset}
            Supports DST: {timezone.SupportsDaylightSavingTime}
            """;
    }
}
