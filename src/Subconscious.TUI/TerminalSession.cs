namespace Subconscious.TUI;

/// <summary>Restores the user's normal terminal buffer when disposed.</summary>
public sealed class TerminalSession : IDisposable
{
    private bool _disposed;

    public TerminalSession()
    {
        Terminal.EnterAlternateBuffer();
        Terminal.HideCursor();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Terminal.Reset();
        Terminal.ShowCursor();
        Terminal.ExitAlternateBuffer();
    }
}
