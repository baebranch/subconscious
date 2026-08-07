using System.Diagnostics;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: Subconscious.Desktop.UITests <screenshot-path> [desktop-pid]");
    return 2;
}

try
{
    Process process;
    if (args.Length == 2)
    {
        if (!int.TryParse(args[1], out var processId) || processId <= 0)
        {
            Console.Error.WriteLine($"Invalid desktop PID: '{args[1]}'.");
            return 2;
        }

        process = Process.GetProcessById(processId);
        if (!string.Equals(process.ProcessName, "Subconscious.Desktop", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"PID {processId} is '{process.ProcessName}', not Subconscious.Desktop.");
            return 2;
        }
    }
    else
    {
        process = Process.GetProcessesByName("Subconscious.Desktop")
            .OrderByDescending(candidate => candidate.StartTime)
            .FirstOrDefault(candidate => candidate.MainWindowHandle != IntPtr.Zero)
            ?? throw new InvalidOperationException("No visible Subconscious.Desktop window is running.");
    }

    process.Refresh();
    if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
    {
        throw new InvalidOperationException($"Subconscious.Desktop PID {process.Id} has no visible top-level window.");
    }

    var windowHandle = process.MainWindowHandle;
    var outputPath = Path.GetFullPath(args[0]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    Console.WriteLine($"Attaching to PID {process.Id}, HWND 0x{windowHandle.ToInt64():X}.");

    var options = new AppiumOptions();
    options.PlatformName = "Windows";
    options.AutomationName = "Windows";
    options.AddAdditionalAppiumOption("appTopLevelWindow", windowHandle.ToInt64().ToString("x"));

    var wadUrl = Environment.GetEnvironmentVariable("WIN_APP_DRIVER_URL");
    if (!string.IsNullOrWhiteSpace(wadUrl))
    {
        // Use a caller-supplied WinAppDriver service when validation runs from a portable,
        // non-admin artifact instead of a system-wide MSI installation.
        options.AddAdditionalAppiumOption("wadUrl", wadUrl);
    }

    var serverUrl = Environment.GetEnvironmentVariable("APPIUM_SERVER_URL") ?? "http://127.0.0.1:4723/";
    using var driver = new WindowsDriver(new Uri(serverUrl), options);
    Console.WriteLine($"Window title: {driver.Title}");
    driver.GetScreenshot().SaveAsFile(outputPath);
    Console.WriteLine($"Screenshot written to {outputPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Appium screenshot failed: {ex}");
    return 1;
}
