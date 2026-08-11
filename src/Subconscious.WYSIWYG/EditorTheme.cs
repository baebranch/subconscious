namespace Subconscious.WYSIWYG;

/// <summary>Host-supplied palette; the library never depends on an application's theme service.</summary>
public sealed record EditorTheme(
    Color Surface,
    Color Panel,
    Color Text,
    Color MutedText,
    Color Divider,
    Color Hover,
    Color Accent,
    Color Selection)
{
    public Color SyntaxKeyword { get; init; } = Color.FromArgb("#8250DF");
    public Color SyntaxString { get; init; } = Color.FromArgb("#0A7D36");
    public Color SyntaxNumber { get; init; } = Color.FromArgb("#0550AE");

    public static EditorTheme Light { get; } = new(
        Color.FromArgb("#FFFFFF"), Color.FromArgb("#FFFFFF"), Color.FromArgb("#1F1B2E"),
        Color.FromArgb("#8A8698"), Color.FromArgb("#E5E3ED"), Color.FromArgb("#EFEEF4"),
        Color.FromArgb("#673AB7"), Color.FromArgb("#EDE7F6"));

    public static EditorTheme Dark { get; } = new(
        Color.FromArgb("#2C2C2C"), Color.FromArgb("#2C2C2C"), Color.FromArgb("#F5F5F5"),
        Color.FromArgb("#C4C4C4"), Color.FromArgb("#454545"), Color.FromArgb("#383838"),
        Color.FromArgb("#9B82DB"), Color.FromArgb("#403653"))
    {
        SyntaxKeyword = Color.FromArgb("#C69BF7"),
        SyntaxString = Color.FromArgb("#7EE787"),
        SyntaxNumber = Color.FromArgb("#79C0FF"),
    };
}
