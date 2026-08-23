using System.Runtime.InteropServices;

namespace Subconscious.TUI;

/// <summary>Reads native Windows console wheel records without changing the cross-platform key fallback.</summary>
internal sealed class WindowsConsoleMouseInput : IDisposable
{
    private const int StandardInputHandle = -10;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;
    private const ushort KeyEvent = 0x0001;
    private const ushort MouseEvent = 0x0002;
    private const uint MouseWheeled = 0x0004;
    private static readonly IntPtr InvalidHandle = new(-1);

    private readonly IntPtr _inputHandle;
    private readonly uint _originalMode;
    private bool _disposed;

    private WindowsConsoleMouseInput(IntPtr inputHandle, uint originalMode)
    {
        _inputHandle = inputHandle;
        _originalMode = originalMode;
    }

    public static WindowsConsoleMouseInput? TryEnable()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
        {
            return null;
        }

        var inputHandle = GetStdHandle(StandardInputHandle);
        if (inputHandle == IntPtr.Zero || inputHandle == InvalidHandle || !GetConsoleMode(inputHandle, out var mode))
        {
            return null;
        }

        var mouseMode = (mode | EnableMouseInput | EnableExtendedFlags) & ~EnableQuickEditMode;
        return SetConsoleMode(inputHandle, mouseMode) ? new WindowsConsoleMouseInput(inputHandle, mode) : null;
    }

    public bool TryReadWheel(out int delta)
    {
        delta = 0;
        while (PeekConsoleInputW(_inputHandle, out var record, 1, out var count) && count > 0)
        {
            if (record.EventType == KeyEvent)
            {
                return false;
            }

            if (!ReadConsoleInputW(_inputHandle, out record, 1, out count) || count == 0)
            {
                return false;
            }

            if (record.EventType == MouseEvent && record.MouseEvent.EventFlags == MouseWheeled)
            {
                delta = (short)(record.MouseEvent.ButtonState >> 16);
                return delta != 0;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            SetConsoleMode(_inputHandle, _originalMode);
        }
        catch
        {
            // Terminal output restoration must still proceed if the host detached the console.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekConsoleInputW(IntPtr consoleInput, out InputRecord buffer, uint length, out uint eventsRead);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleInputW(IntPtr consoleInput, out InputRecord buffer, uint length, out uint eventsRead);

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public MouseEventRecord MouseEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseEventRecord
    {
        public Coord MousePosition;
        public uint ButtonState;
        public uint ControlKeyState;
        public uint EventFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }
}
