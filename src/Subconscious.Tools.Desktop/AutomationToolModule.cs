using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using System.ComponentModel;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// Automation tool module. Provides automation capabilities like running scripts and scheduling.
/// Port of Python's <c>desktop_tools/automation.py</c>.
/// </summary>
public sealed class AutomationToolModule : IToolModule
{
    public string Slug => "automation";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                RunScript,
                "run_script",
                "Execute a script file (Python, PowerShell, batch, etc.)."),

            AIFunctionFactory.Create(
                ListScheduledTasks,
                "list_scheduled_tasks",
                "List scheduled tasks (Windows only).")
        ];
    }

    private static string RunScript(
        EngineContext context,
        [Description("Path to the script file.")] string path,
        [Description("Optional script arguments.")] string arguments = "")
    {
        try
        {
            if (!File.Exists(path))
            {
                return $"Error: Script not found: '{path}'";
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            string fileName;
            string argumentsPart;

            switch (extension)
            {
                case ".py":
                    fileName = "python";
                    argumentsPart = $"\"{path}\" {arguments}";
                    break;
                case ".ps1":
                    fileName = "powershell.exe";
                    argumentsPart = $"-ExecutionPolicy Bypass -File \"{path}\" {arguments}";
                    break;
                case ".bat":
                case ".cmd":
                    fileName = path;
                    argumentsPart = arguments;
                    break;
                default:
                    // Try to execute with default handler
                    fileName = path;
                    argumentsPart = arguments;
                    break;
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = argumentsPart,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            process.Start();

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var output = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(stdout))
            {
                output.AppendLine("Output:");
                output.AppendLine(stdout.TrimEnd());
            }

            if (!string.IsNullOrEmpty(stderr))
            {
                output.AppendLine("Errors:");
                output.AppendLine(stderr.TrimEnd());
            }

            if (process.ExitCode != 0)
            {
                output.AppendLine($"\nExit code: {process.ExitCode}");
            }

            return output.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error running script: {ex.Message}";
        }
    }

    private static string ListScheduledTasks(EngineContext context)
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return "Scheduled tasks are only supported on Windows";
        }

        try
        {
            // For Windows, we could use TaskScheduler library or invoke schtasks.exe
            // For now, return a placeholder message
            return "List scheduled tasks: schtasks.exe query /fo LIST /v";
        }
        catch (Exception ex)
        {
            return $"Error listing scheduled tasks: {ex.Message}";
        }
    }
}
