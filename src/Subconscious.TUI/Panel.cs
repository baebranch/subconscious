namespace Subconscious.TUI;

/// <summary>A bordered rectangular container rendered with Unicode box drawing characters.</summary>
public sealed class Panel : Widget
{
    public Panel(string title = "") => Title = title;

    public string Title { get; set; }
    public ConsoleColor BorderColor { get; set; } = ConsoleColor.DarkCyan;

    public override void Render()
    {
        var area = Bounds;
        if (area.Width < 2 || area.Height < 2)
        {
            return;
        }

        Terminal.SetForeground(BorderColor);
        Terminal.MoveTo(area.Left, area.Top);
        Console.Write($"┌{new string('─', area.Width - 2)}┐");
        for (var row = area.Top + 1; row < area.Bottom; row++)
        {
            Terminal.MoveTo(area.Left, row);
            Console.Write("│");
            Terminal.MoveTo(area.Right, row);
            Console.Write("│");
        }

        Terminal.MoveTo(area.Left, area.Bottom);
        Console.Write($"└{new string('─', area.Width - 2)}┘");
        if (!string.IsNullOrWhiteSpace(Title) && area.Width > 6)
        {
            Terminal.MoveTo(area.Left + 2, area.Top);
            Console.Write($" {Title[..Math.Min(Title.Length, area.Width - 6)]} ");
        }

        Terminal.Reset();
    }
}
