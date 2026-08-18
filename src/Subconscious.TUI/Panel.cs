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
        Terminal.Write(BuildTopBorder(area.Width));
        for (var row = area.Top + 1; row < area.Bottom; row++)
        {
            Terminal.MoveTo(area.Left, row);
            Terminal.Write($"│{new string(' ', area.Width - 2)}│");
        }

        Terminal.MoveTo(area.Left, area.Bottom);
        Terminal.Write($"└{new string('─', area.Width - 2)}┘");
        Terminal.Reset();
    }

    private string BuildTopBorder(int width)
    {
        var border = $"┌{new string('─', width - 2)}┐".ToCharArray();
        if (string.IsNullOrWhiteSpace(Title) || width <= 6)
        {
            return new string(border);
        }

        var visibleTitle = Title[..Math.Min(Title.Length, width - 6)];
        $" {visibleTitle} ".CopyTo(0, border, 2, visibleTitle.Length + 2);
        return new string(border);
    }
}
