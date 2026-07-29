using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using System.ComponentModel;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// Terminal/command execution tool module. Provides safe command execution capabilities.
/// Port of Python's <c>desktop_tools/terminal.py</c>.
/// </summary>
public sealed class TerminalToolModule : IToolModule
{
    public string Slug => "terminal";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                RunCommand,
                "run_command",
                "Execute a shell command and return the output. Use with caution - commands run with user permissions."),

            AIFunctionFactory.Create(
                RunPowershell,
                "run_powershell",
                "Execute a PowerShell command and return the output."),

            AIFunctionFactory.Create(
                ListProcesses,
                "list_processes",
                "List running processes. Optionally filter by process name."),

            AIFunctionFactory.Create(
                KillProcess,
                "kill_process",
                "Terminate a process by PID.")
        ];
    }

    private static string RunCommand(
        [Description("The shell command to execute (bash/sh on Unix, cmd on Windows).")] string command,
        EngineContext context)
    {
        return ExecuteCommand(command, useShell: false);
    }

    private static string RunPowershell(
        [Description("The PowerShell command to execute.")] string command,
        EngineContext context)
    {
        return ExecuteCommand(command, useShell: true);
    }

    private static string ListProcesses(
        [Description("Optional process name filter.")] string? filter = null)
    {
        try
        {
            var processes = System.Diagnostics.Process.GetProcesses();
            var output = new System.Text.StringBuilder();

            foreach (var proc in processes.OrderBy(p => p.ProcessName))
            {
                if (string.IsNullOrEmpty(filter) || proc.ProcessName.Contains(filter!, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        output.AppendLine($"{proc.ProcessName,-20} PID: {proc.Id,-8} Memory: {proc.WorkingSet64 / 1024 / 1024,6:N1} MB");
                    }
                    catch
                    {
                        // Access denied to some system processes
                    }
                }
            }

            return output.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error listing processes: {ex.Message}";
        }
    }

    private static string KillProcess(
        [Description("The process ID (PID) to terminate.")] int pid)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(pid);
            var name = process.ProcessName;
            process.Kill();
            return $"Successfully terminated process '{name}' (PID: {pid})";
        }
        catch (ArgumentException)
        {
            return $"Error: Process with PID {pid} not found";
        }
        catch (Exception ex)
        {
            return $"Error terminating process {pid}: {ex.Message}";
        }
    }

    private static string ExecuteCommand(string command, bool useShell)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "Error: Command cannot be empty";
        }

        // Security check: block dangerous commands
        var dangerousPatterns = new[] { "rm -rf", "format", "del /f /s /q", "shutdown", "reboot" };
        var lowerCommand = command.ToLowerInvariant();

        foreach (var pattern in dangerousPatterns)
        {
            if (lowerCommand.Contains(pattern))
            {
                return $"Error: Command '{command}' is blocked for security reasons";
            }
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = useShell ? "powershell.exe" : (IsWindows() ? "cmd.exe" : "/bin/bash"),
                Arguments = useShell ? $"/c {command}" : (IsWindows() ? $"/c {command}" : $"-c \"{command}\""),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
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
            return $"Error executing command: {ex.Message}";
        }
    }

    private static bool IsWindows() => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.Windows);
}
