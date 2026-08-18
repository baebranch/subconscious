namespace Subconscious.TUI;

/// <summary>Restores the user's normal terminal buffer when disposed.</summary>
public sealed class TerminalSession : IDisposable
{
    private readonly WindowsConsoleMouseInput? _mouseInput;
    private bool _disposed;

    public TerminalSession()
    {
        _mouseInput = WindowsConsoleMouseInput.TryEnable();
        Terminal.EnterAlternateBuffer();
        Terminal.HideCursor();
    }

    internal bool TryReadMouseWheel(out int delta) =>
        _mouseInput?.TryReadWheel(out delta) ?? ReturnNoWheel(out delta);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mouseInput?.Dispose();
        Terminal.Reset();
        Terminal.ShowCursor();
        Terminal.ExitAlternateBuffer();
    }

    private static bool ReturnNoWheel(out int delta)
    {
        delta = 0;
        return false;
    }
}
