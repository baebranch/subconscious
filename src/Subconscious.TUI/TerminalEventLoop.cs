namespace Subconscious.TUI;

/// <summary>Polls terminal input and size, redraws the root widget, and restores the screen on exit.</summary>
public sealed class TerminalEventLoop
{
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <param name="continueAfterKey">Return false to leave the loop after processing a key.</param>
    public void Run(Widget root, Func<ConsoleKeyInfo, bool>? continueAfterKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var previousSize = new TerminalSize(0, 0);
        var redraw = true;

        using var session = Terminal.UseAlternateBuffer();
        while (!cancellationToken.IsCancellationRequested)
        {
            var size = Terminal.Size;
            if (size != previousSize)
            {
                previousSize = size;
                root.Resize(new UiRect(1, 1, size.Width, size.Height));
                redraw = true;
            }

            if (redraw)
            {
                Terminal.Clear();
                root.Render();
                Terminal.Reset();
                redraw = false;
            }

            if (TryReadKey(out var key))
            {
                root.OnKey(key);
                redraw = true;
                if (continueAfterKey is not null && !continueAfterKey(key))
                {
                    break;
                }
            }

            Thread.Sleep(IdleDelay);
        }
    }

    private static bool TryReadKey(out ConsoleKeyInfo key)
    {
        try
        {
            if (Console.IsInputRedirected || !Console.KeyAvailable)
            {
                key = default;
                return false;
            }

            key = Console.ReadKey(intercept: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            key = default;
            return false;
        }
    }
}
