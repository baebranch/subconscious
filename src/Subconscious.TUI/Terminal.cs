using System.Text;

namespace Subconscious.TUI;

/// <summary>ANSI terminal operations that work on Windows, Linux, and macOS terminals.</summary>
public static class Terminal
{
    private const string Escape = "\u001b[";

    public static TerminalSize Size
    {
        get
        {
            try { return new TerminalSize(Math.Max(1, Console.WindowWidth), Math.Max(1, Console.WindowHeight)); }
            catch (IOException) { return new TerminalSize(80, 24); }
        }
    }

    public static void EnterAlternateBuffer() => Write("\u001b[?1049h\u001b[H");
    public static void ExitAlternateBuffer() => Write("\u001b[?1049l");
    public static void HideCursor() => Write("\u001b[?25l");
    public static void ShowCursor() => Write("\u001b[?25h");
    public static void Clear() => Write("\u001b[2J\u001b[H");
    public static void ClearLine() => Write("\u001b[2K");
    public static void Reset() => Write("\u001b[0m");
    public static void MoveTo(int column, int row) => Write($"{Escape}{Math.Max(1, row)};{Math.Max(1, column)}H");
    public static void MoveUp(int count = 1) => Write($"{Escape}{Math.Max(1, count)}A");
    public static void MoveDown(int count = 1) => Write($"{Escape}{Math.Max(1, count)}B");
    public static void MoveRight(int count = 1) => Write($"{Escape}{Math.Max(1, count)}C");
    public static void MoveLeft(int count = 1) => Write($"{Escape}{Math.Max(1, count)}D");

    public static void SetForeground(ConsoleColor color) => Write($"{Escape}{ForegroundCode(color)}m");
    public static void SetBackground(ConsoleColor color) => Write($"{Escape}{BackgroundCode(color)}m");

    private static readonly AsyncLocal<StringBuilder?> FrameBuffer = new();

    public static TerminalSession UseAlternateBuffer() => new();

    /// <summary>Buffers ANSI output until the returned frame is disposed and emits it in one console write.</summary>
    public static TerminalFrame BeginFrame()
    {
        if (FrameBuffer.Value is not null)
        {
            throw new InvalidOperationException("Terminal frames cannot be nested.");
        }

        var buffer = new StringBuilder();
        FrameBuffer.Value = buffer;
        return new TerminalFrame(buffer);
    }

    public static void Write(string value)
    {
        var buffer = FrameBuffer.Value;
        if (buffer is null)
        {
            Console.Write(value);
            return;
        }

        buffer.Append(value);
    }

    private static int ForegroundCode(ConsoleColor color) => color is >= ConsoleColor.DarkGray
        ? 90 + ((int)color - (int)ConsoleColor.DarkGray) : 30 + (int)color;

    private static int BackgroundCode(ConsoleColor color) => color is >= ConsoleColor.DarkGray
        ? 100 + ((int)color - (int)ConsoleColor.DarkGray) : 40 + (int)color;

    public sealed class TerminalFrame : IDisposable
    {
        private readonly StringBuilder _buffer;
        private bool _disposed;

        internal TerminalFrame(StringBuilder buffer) => _buffer = buffer;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            FrameBuffer.Value = null;
            Console.Write(_buffer.ToString());
        }
    }
}

public readonly record struct TerminalSize(int Width, int Height);
