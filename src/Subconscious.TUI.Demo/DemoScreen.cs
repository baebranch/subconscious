using Subconscious.TUI;

namespace Subconscious.TUI.Demo;

internal sealed class DemoScreen : Widget
{
    private static readonly string[] Messages =
    {
        "System ready. This scrollable transcript is rendered inside the content panel.",
        "Use Up and Down to move through prior messages.",
        "Press M to open the action menu, then use arrows and Enter to select.",
        "The title and composer are pinned while the transcript moves between them.",
        "Resize this terminal at any time; the layout will be recalculated.",
        "The Send button is focused and responds to Enter or Space.",
        "All drawing comes from the dependency-free Subconscious.TUI ANSI renderer.",
        "Press Q to leave the demonstration and restore your original terminal." 
    };

    private readonly Panel _titlePanel = new("Subconscious.TUI · pinned title");
    private readonly Panel _contentPanel = new("Transcript · scrollable");
    private readonly Panel _composerPanel = new("Composer · pinned");
    private readonly Button _sendButton;
    private UiRect _contentArea;
    private UiRect _composerArea;
    private int _scrollOffset;
    private bool _menuOpen;
    private int _menuIndex;
    private string _status = "Ready — M: menu · ↑↓: scroll · Enter: send · Q: quit";

    public DemoScreen()
    {
        _sendButton = new Button("Send", Send) { IsFocused = true };
    }

    public override void Resize(UiRect bounds)
    {
        base.Resize(bounds);
        var composerHeight = Math.Clamp(bounds.Height / 4, 4, 6);
        var headerHeight = 3;
        _composerArea = new UiRect(bounds.Left, Math.Max(bounds.Top, bounds.Bottom - composerHeight + 1), bounds.Width, composerHeight);
        _contentArea = new UiRect(bounds.Left, bounds.Top + headerHeight + 1, bounds.Width,
            Math.Max(3, _composerArea.Top - (bounds.Top + headerHeight + 2)));
        _titlePanel.Resize(new UiRect(bounds.Left, bounds.Top, bounds.Width, headerHeight));
        _contentPanel.Resize(_contentArea);
        _composerPanel.Resize(_composerArea);
        _sendButton.Resize(new UiRect(Math.Max(bounds.Left + 2, bounds.Right - 10), Math.Max(bounds.Top, bounds.Bottom - 2), 9, 1));
    }

    public override void Render()
    {
        if (Bounds.Width < 30 || Bounds.Height < 12)
        {
            WriteAt(Bounds.Left, Bounds.Top, "Resize terminal to at least 30×12.", Bounds.Width);
            return;
        }

        _titlePanel.Render();
        Terminal.SetForeground(ConsoleColor.Cyan);
        WriteAt(Bounds.Left + 2, Bounds.Top + 1, "Alternate buffer · ANSI colors · resize-aware event loop", Bounds.Width - 4);
        Terminal.Reset();
        _contentPanel.Render();
        RenderTranscript();
        _composerPanel.Render();
        RenderComposer();
        _sendButton.Render();
        if (_menuOpen)
        {
            RenderMenu();
        }
    }

    public override bool OnKey(ConsoleKeyInfo key)
    {
        if (_menuOpen)
        {
            return HandleMenuKey(key);
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                return ScrollBy(-1);
            case ConsoleKey.DownArrow:
                return ScrollBy(1);
            case ConsoleKey.M:
                _menuOpen = true;
                _status = "Menu open — choose an action with ↑↓ and Enter.";
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                return _sendButton.OnKey(key);
            default:
                return false;
        }
    }

    public override bool OnScroll(TerminalScrollEvent scroll)
    {
        if (_menuOpen)
        {
            return false;
        }

        var availableLines = Math.Max(1, _contentArea.Height - 2);
        var delta = scroll.IsPageScroll ? scroll.Delta * availableLines : scroll.Delta;
        return ScrollBy(delta);
    }

    private void RenderTranscript()
    {
        var firstLine = _contentArea.Top + 1;
        var availableLines = Math.Max(0, _contentArea.Height - 2);
        for (var index = 0; index < availableLines; index++)
        {
            var messageIndex = _scrollOffset + index;
            if (messageIndex >= Messages.Length)
            {
                break;
            }

            Terminal.SetForeground(ConsoleColor.Gray);
            WriteAt(_contentArea.Left + 2, firstLine + index, $"{messageIndex + 1:00}  {Messages[messageIndex]}", _contentArea.Width - 4);
        }

        Terminal.SetForeground(ConsoleColor.DarkGray);
        WriteAt(_contentArea.Right - 13, _contentArea.Bottom - 1, $"{_scrollOffset + 1}/{Messages.Length} ↑↓", 12);
        Terminal.Reset();
    }

    private bool ScrollBy(int delta)
    {
        var previousOffset = _scrollOffset;
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, Messages.Length - 1);
        return _scrollOffset != previousOffset;
    }

    private void RenderComposer()
    {
        Terminal.SetForeground(ConsoleColor.White);
        WriteAt(_composerArea.Left + 2, _composerArea.Top + 1, "> Ask Subconscious something…", _composerArea.Width - 15);
        Terminal.SetForeground(ConsoleColor.DarkGray);
        WriteAt(_composerArea.Left + 2, _composerArea.Bottom - 1, _status, _composerArea.Width - 4);
        Terminal.Reset();
    }

    private void Send()
    {
        _status = "Sent a demo message at " + DateTime.Now.ToString("T") + ".";
    }

    private bool HandleMenuKey(ConsoleKeyInfo key)
    {
        var choices = new[] { "New conversation", "Copy transcript", "Toggle theme", "Close menu" };
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _menuIndex = (_menuIndex + choices.Length - 1) % choices.Length;
                return true;
            case ConsoleKey.DownArrow:
                _menuIndex = (_menuIndex + 1) % choices.Length;
                return true;
            case ConsoleKey.Escape:
                _menuOpen = false;
                _status = "Menu dismissed.";
                return true;
            case ConsoleKey.Enter:
                _menuOpen = false;
                _status = choices[_menuIndex] == "Close menu" ? "Menu dismissed." : $"Selected: {choices[_menuIndex]}.";
                return true;
            default:
                return true;
        }
    }

    private void RenderMenu()
    {
        var choices = new[] { "New conversation", "Copy transcript", "Toggle theme", "Close menu" };
        var width = Math.Min(28, Bounds.Width - 4);
        var height = choices.Length + 3;
        var left = Bounds.Left + (Bounds.Width - width) / 2;
        var top = Bounds.Top + Math.Max(1, (Bounds.Height - height) / 2);
        var popup = new Panel("Actions");
        popup.Resize(new UiRect(left, top, width, height));
        popup.Render();

        for (var index = 0; index < choices.Length; index++)
        {
            var selected = index == _menuIndex;
            Terminal.SetForeground(selected ? ConsoleColor.Black : ConsoleColor.White);
            Terminal.SetBackground(selected ? ConsoleColor.Cyan : ConsoleColor.Black);
            WriteAt(left + 2, top + 1 + index, $"{(selected ? "›" : " ")} {choices[index]}", width - 4);
            Terminal.Reset();
        }
    }
}
