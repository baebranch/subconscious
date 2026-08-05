namespace Subconscious.Desktop.ViewModels;

/// <summary>The physical edge that hosts the persistent desktop navigation rail.</summary>
public enum SidebarPosition
{
    Left,
    Right,
}

/// <summary>A picker-friendly sidebar position with the canonical persisted display value.</summary>
public sealed record SidebarPositionOption(SidebarPosition Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public static class SidebarPositionCatalog
{
    public static IReadOnlyList<SidebarPositionOption> Options { get; } =
    [
        new(SidebarPosition.Left, "left"),
        new(SidebarPosition.Right, "right"),
    ];

    public static SidebarPositionOption OptionFor(SidebarPosition position) =>
        Options.First(option => option.Value == position);
}
