using System.Globalization;

namespace Subconscious.Terminal;

internal enum TerminalThemeMode
{
    System,
    Light,
    Dark,
}

internal enum TerminalAccent
{
    Purple,
    Blue,
    Teal,
    Green,
    Yellow,
    Orange,
    Red,
    Pink,
}

internal sealed record TerminalTheme(TerminalThemeMode Mode, TerminalAccent Accent)
{
    private static readonly IReadOnlyDictionary<TerminalAccent, Rgb> AccentSeeds =
        new Dictionary<TerminalAccent, Rgb>
        {
            [TerminalAccent.Purple] = Rgb.Parse("#673AB7"),
            [TerminalAccent.Blue] = Rgb.Parse("#2196F3"),
            [TerminalAccent.Teal] = Rgb.Parse("#009688"),
            [TerminalAccent.Green] = Rgb.Parse("#4CAF50"),
            [TerminalAccent.Yellow] = Rgb.Parse("#F9A825"),
            [TerminalAccent.Orange] = Rgb.Parse("#FB8C00"),
            [TerminalAccent.Red] = Rgb.Parse("#E53935"),
            [TerminalAccent.Pink] = Rgb.Parse("#D81B60"),
        };

    public static TerminalTheme Default { get; } = new(TerminalThemeMode.System, TerminalAccent.Purple);
    public string ModeValue => Mode.ToString().ToLowerInvariant();
    public string AccentValue => Accent.ToString().ToLowerInvariant();
    public string DisplayName => $"{Mode} · {Accent}";

    public TerminalPalette Palette
    {
        get
        {
            var seed = AccentSeeds[Accent];
            if (Mode == TerminalThemeMode.System)
            {
                return new TerminalPalette(
                    ThemeColor.From(seed), ThemeColor.From(seed), ThemeColor.Default,
                    ThemeColor.Grey, ThemeColor.Red, ThemeColor.Yellow, ThemeColor.Grey,
                    ThemeColor.From(seed), ThemeColor.From(seed));
            }

            var dark = Mode == TerminalThemeMode.Dark;
            var accent = dark ? seed.Blend(Rgb.White, 0.25) : seed.Blend(Rgb.Black, 0.08);
            return new TerminalPalette(
                ThemeColor.From(accent), ThemeColor.From(accent),
                ThemeColor.From(Rgb.Parse(dark ? "#F5F5F5" : "#1F1B2E")),
                ThemeColor.From(Rgb.Parse(dark ? "#C4C4C4" : "#8A8698")),
                ThemeColor.From(Rgb.Parse(dark ? "#FF8A80" : "#D9534F")),
                ThemeColor.From(Rgb.Parse(dark ? "#F9A825" : "#B26A00")),
                ThemeColor.From(Rgb.Parse(dark ? "#C4C4C4" : "#5C566A")),
                ThemeColor.From(accent), ThemeColor.From(accent));
        }
    }

    public static TerminalThemeMode ParseMode(string? value) =>
        value is not null && TryParseMode(value, out var mode) ? mode : TerminalThemeMode.System;

    public static TerminalAccent ParseAccent(string? value) =>
        value is not null && TryParseAccent(value, out var accent) ? accent : TerminalAccent.Purple;

    public static bool TryParseMode(string value, out TerminalThemeMode mode) =>
        Enum.TryParse(value, true, out mode) && Enum.IsDefined(mode);

    public static bool TryParseAccent(string value, out TerminalAccent accent) =>
        Enum.TryParse(value, true, out accent) && Enum.IsDefined(accent);
}

internal sealed record TerminalPalette(
    ThemeColor Accent,
    ThemeColor User,
    ThemeColor Assistant,
    ThemeColor Muted,
    ThemeColor Error,
    ThemeColor Warning,
    ThemeColor Code,
    ThemeColor Stream,
    ThemeColor Composer);

internal sealed record ThemeColor(string Markup, string Sgr)
{
    public static ThemeColor Default { get; } = new("default", "39");
    public static ThemeColor Grey { get; } = new("grey", "90");
    public static ThemeColor Red { get; } = new("red", "31");
    public static ThemeColor Yellow { get; } = new("yellow", "33");

    public static ThemeColor From(Rgb value) => new(value.Hex, $"38;2;{value.Red};{value.Green};{value.Blue}");

    public string Paint(string text, bool bold = false, bool dim = false)
    {
        var modifiers = bold ? "1;" : dim ? "2;" : string.Empty;
        return $"\u001b[{modifiers}{Sgr}m{text}\u001b[0m";
    }
}

internal readonly record struct Rgb(byte Red, byte Green, byte Blue)
{
    public static Rgb Black { get; } = new(0, 0, 0);
    public static Rgb White { get; } = new(255, 255, 255);
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public static Rgb Parse(string hex) => new(
        byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    public Rgb Blend(Rgb target, double amount) => new(
        BlendChannel(Red, target.Red, amount),
        BlendChannel(Green, target.Green, amount),
        BlendChannel(Blue, target.Blue, amount));

    private static byte BlendChannel(byte source, byte target, double amount) =>
        (byte)Math.Clamp((int)Math.Round(source + ((target - source) * amount)), 0, 255);
}
