using System.Text;

namespace Subconscious.Terminal;

internal enum ComposerAction
{
    None,
    Changed,
    Submit,
}

internal sealed class Composer
{
    private readonly StringBuilder _text = new();
    private int _caret;

    public string Text => _text.ToString();
    public int Caret => _caret;
    public bool IsEmpty => _text.Length == 0;

    public ComposerAction Apply(ConsoleKeyInfo key)
    {
        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        var shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);

        if (key.Key == ConsoleKey.Enter)
        {
            if (shift || control) { Insert("\n"); return ComposerAction.Changed; }
            return ComposerAction.Submit;
        }
        if (control && key.Key == ConsoleKey.J) { Insert("\n"); return ComposerAction.Changed; }
        if (key.Key == ConsoleKey.LeftArrow) { _caret = TerminalText.PreviousElement(Text, _caret); return ComposerAction.Changed; }
        if (key.Key == ConsoleKey.RightArrow) { _caret = TerminalText.NextElement(Text, _caret); return ComposerAction.Changed; }
        if (key.Key == ConsoleKey.Home) { MoveToLineStart(); return ComposerAction.Changed; }
        if (key.Key == ConsoleKey.End) { MoveToLineEnd(); return ComposerAction.Changed; }

        if (key.Key == ConsoleKey.Backspace && _caret > 0)
        {
            var previous = TerminalText.PreviousElement(Text, _caret);
            _text.Remove(previous, _caret - previous);
            _caret = previous;
            return ComposerAction.Changed;
        }
        if (key.Key == ConsoleKey.Delete && _caret < _text.Length)
        {
            var next = TerminalText.NextElement(Text, _caret);
            _text.Remove(_caret, next - _caret);
            return ComposerAction.Changed;
        }

        if (!control && key.KeyChar is >= ' ' and not '\u007f')
        {
            Insert(key.KeyChar.ToString());
            return ComposerAction.Changed;
        }
        return ComposerAction.None;
    }

    public void Replace(string value)
    {
        _text.Clear();
        _text.Append(value);
        _caret = _text.Length;
    }

    public string Take()
    {
        var value = Text;
        Clear();
        return value;
    }

    public void Clear() => Replace(string.Empty);

    private void Insert(string value)
    {
        _text.Insert(_caret, value);
        _caret += value.Length;
    }

    private void MoveToLineStart()
    {
        var value = Text;
        var newline = _caret == 0 ? -1 : value.LastIndexOf('\n', _caret - 1);
        _caret = newline + 1;
    }

    private void MoveToLineEnd()
    {
        var newline = Text.IndexOf('\n', _caret);
        _caret = newline < 0 ? _text.Length : newline;
    }
}
