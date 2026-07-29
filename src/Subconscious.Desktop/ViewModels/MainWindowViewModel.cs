namespace Subconscious.Desktop.ViewModels;

/// <summary>
/// Root view model for the main window. Only the chat pane is wired up for this vertical
/// slice (translation.md Phase 6) — the center utility panel and right context panel from
/// the gui.pen design are static placeholders until their own passes.
/// </summary>
public sealed class MainWindowViewModel
{
    public ChatViewModel Chat { get; } = new();
}
