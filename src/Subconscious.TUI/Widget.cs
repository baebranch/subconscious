namespace Subconscious.TUI;

/// <summary>Base class for an ANSI-rendered terminal UI component.</summary>
public abstract class Widget
{
    public UiRect Bounds { get; private set; }

    public virtual void Resize(UiRect bounds) => Bounds = bounds;

    /// <summary>Writes this widget to <see cref="Console"/> using ANSI terminal operations.</summary>
    public abstract void Render();

    /// <summary>Handles a captured key. Return true when the widget state changed.</summary>
    public virtual bool OnKey(ConsoleKeyInfo key) => false;

    protected void WriteAt(int column, int row, string text, int maximumWidth)
    {
        if (maximumWidth <= 0 || row < Bounds.Top || row > Bounds.Bottom)
        {
            return;
        }

        var visible = text.Length <= maximumWidth ? text : text[..maximumWidth];
        Terminal.MoveTo(column, row);
        Console.Write(visible);
    }
}

public readonly record struct UiRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width - 1;
    public int Bottom => Top + Height - 1;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
