namespace Subconscious.TUI;

/// <summary>A focusable, single-line button activated by Enter or Space.</summary>
public sealed class Button : Widget
{
    public Button(string text, Action? activated = null)
    {
        Text = text;
        Activated = activated;
    }

    public string Text { get; set; }
    public bool IsFocused { get; set; }
    public Action? Activated { get; set; }

    public override bool OnKey(ConsoleKeyInfo key)
    {
        if (!IsFocused || (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Spacebar))
        {
            return false;
        }

        Activated?.Invoke();
        return true;
    }

    public override void Render()
    {
        if (Bounds.Width < 4 || Bounds.Height < 1)
        {
            return;
        }

        var label = $"[ {Text} ]";
        var visible = label[..Math.Min(label.Length, Bounds.Width)];
        Terminal.SetForeground(IsFocused ? ConsoleColor.Black : ConsoleColor.Cyan);
        Terminal.SetBackground(IsFocused ? ConsoleColor.Cyan : ConsoleColor.Black);
        Terminal.MoveTo(Bounds.Left, Bounds.Top);
        Terminal.Write(visible.PadRight(Bounds.Width));
        Terminal.Reset();
    }
}
