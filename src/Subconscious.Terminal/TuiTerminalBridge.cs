namespace Subconscious.Terminal;

internal static class TuiTerminal
{
    public static void HideCursor() => global::Subconscious.TUI.Terminal.HideCursor();
    public static void ShowCursor() => global::Subconscious.TUI.Terminal.ShowCursor();
    public static void MoveTo(int column, int row) => global::Subconscious.TUI.Terminal.MoveTo(column, row);
    public static void Write(string value) => global::Subconscious.TUI.Terminal.Write(value);
    public static void Reset() => global::Subconscious.TUI.Terminal.Reset();
    public static void SetForeground(ConsoleColor color) => global::Subconscious.TUI.Terminal.SetForeground(color);
    public static void SetDefaultForeground() => global::Subconscious.TUI.Terminal.SetDefaultForeground();
    public static void SetForegroundRgb(byte red, byte green, byte blue) =>
        global::Subconscious.TUI.Terminal.SetForegroundRgb(red, green, blue);
    public static void SetBold() => global::Subconscious.TUI.Terminal.SetBold();
    public static void SetDim() => global::Subconscious.TUI.Terminal.SetDim();
    public static void SetInverse() => global::Subconscious.TUI.Terminal.SetInverse();
}
