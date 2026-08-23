namespace Subconscious.TUI;

/// <summary>Restores the user's normal terminal buffer when disposed.</summary>
public sealed class TerminalSession : IDisposable
{
    private readonly WindowsConsoleMouseInput? _mouseInput;
    private bool _disposed;

    public TerminalSession()
    {
        WindowsConsoleMouseInput? mouseInput = null;
        try
        {
            mouseInput = WindowsConsoleMouseInput.TryEnable();
            Terminal.EnterInteractiveSession();
            _mouseInput = mouseInput;
        }
        catch
        {
            TryRestoreTerminal();
            mouseInput?.Dispose();
            throw;
        }
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
        try
        {
            TryRestoreTerminal();
        }
        finally
        {
            _mouseInput?.Dispose();
        }
    }

    private static void TryRestoreTerminal()
    {
        try
        {
            Terminal.ExitInteractiveSession();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // The host may have detached or disposed its terminal during shutdown.
        }
    }

    private static bool ReturnNoWheel(out int delta)
    {
        delta = 0;
        return false;
    }
}
