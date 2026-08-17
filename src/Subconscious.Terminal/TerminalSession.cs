using System.Runtime.InteropServices;
using System.Text;

namespace Subconscious.Terminal;

internal sealed class TerminalSession : IDisposable
{
    private const int StandardInputHandle = -10;
    private const int StandardOutputHandle = -11;
    private const uint EnableProcessedOutput = 0x0001;
    private const uint EnableVirtualTerminalProcessing = 0x0004;
    private static readonly nint InvalidHandleValue = new(-1);
    private readonly bool _originalTreatControlCAsInput;
    private readonly Encoding _originalOutputEncoding;
    private readonly nint _outputHandle;
    private readonly uint _originalOutputMode;
    private readonly bool _restoreOutputMode;
    private bool _disposed;

    private TerminalSession(bool forcePlain)
    {
        _originalOutputEncoding = Console.OutputEncoding;
        var interactive = !forcePlain
            && !Console.IsInputRedirected
            && !Console.IsOutputRedirected
            && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

        if (interactive)
        {
            try
            {
                _originalTreatControlCAsInput = Console.TreatControlCAsInput;
                Console.TreatControlCAsInput = true;
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                TryRestoreConsoleState();
                interactive = false;
            }
        }

        if (interactive && OperatingSystem.IsWindows())
        {
            var inputHandle = GetStdHandle(StandardInputHandle);
            var outputHandle = GetStdHandle(StandardOutputHandle);
            var outputMode = 0u;
            var handlesAreValid = IsValidHandle(inputHandle) && IsValidHandle(outputHandle);
            var modesAreReadable = handlesAreValid
                && GetConsoleMode(inputHandle, out _)
                && GetConsoleMode(outputHandle, out outputMode);
            var modeWasEnabled = modesAreReadable
                && SetConsoleMode(outputHandle, outputMode | EnableProcessedOutput | EnableVirtualTerminalProcessing);

            if (modeWasEnabled)
            {
                _outputHandle = outputHandle;
                _originalOutputMode = outputMode;
                _restoreOutputMode = true;
            }
            else
            {
                TryRestoreConsoleState();
                interactive = false;
            }
        }

        Interactive = interactive;
    }

    public bool Interactive { get; }

    public int Width
    {
        get
        {
            try { return Math.Max(20, Console.WindowWidth); }
            catch (IOException) { return 80; }
        }
    }

    public int Height
    {
        get
        {
            try { return Math.Max(8, Console.WindowHeight); }
            catch (IOException) { return 24; }
        }
    }

    public static TerminalSession Open(bool forcePlain) => new(forcePlain);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Interactive)
        {
            try
            {
                Console.Out.Write("\u001b[0m\u001b[?25h");
                Console.Out.Flush();
            }
            catch (IOException)
            {
                // The host detached its terminal before process shutdown.
            }
            TryRestoreConsoleState();
        }

        if (_restoreOutputMode && OperatingSystem.IsWindows())
        {
            SetConsoleMode(_outputHandle, _originalOutputMode);
        }
    }

    private static bool IsValidHandle(nint handle) => handle != nint.Zero && handle != InvalidHandleValue;

    private void TryRestoreConsoleState()
    {
        try { Console.TreatControlCAsInput = _originalTreatControlCAsInput; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException) { }
        try { Console.OutputEncoding = _originalOutputEncoding; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException) { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint consoleHandle, uint mode);
}
