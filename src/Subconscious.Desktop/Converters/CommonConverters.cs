using System.Globalization;

namespace Subconscious.Desktop.Converters;

/// <summary>
/// MAUI XAML has no "not" operator in binding paths (Avalonia's <c>{Binding !IsBusy}</c>), so
/// inversions go through this instead.
/// </summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>True when the bound value is non-null and, for strings, non-blank — used to hide
/// optional rows like a workspace's description or a form's error text.</summary>
public sealed class IsNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The inverse of <see cref="IsNotEmptyConverter"/>. Its own converter rather than
/// <c>IsNotEmpty</c> plus <c>InvertedBool</c> because MAUI bindings can't chain converters.
/// Used to hide content that a populated value replaces, e.g. the workspace list while an
/// error message is showing in its place.</summary>
public sealed class IsEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            _ => false,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>User bubbles align right, assistant bubbles align left.</summary>
public sealed class BubbleAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? LayoutOptions.End : LayoutOptions.Start;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Picks one of two colours from a bool. Unlike a plain XAML <c>DynamicResource</c> setter, a
/// converter result is only recomputed when the bound source property raises
/// <c>PropertyChanged</c>. Theme-aware consumers use <see cref="SelectedResourceKey"/> and
/// <see cref="UnselectedResourceKey"/> rather than assigning <see cref="SelectedColor"/>/
/// <see cref="UnselectedColor"/> from XAML: the converter looks those keys up in
/// <c>Application.Current.Resources</c> every time it converts, and MainViewModel re-raises the
/// relevant bool properties whenever ThemeService applies a palette. That combination is what
/// lets the context-tab icon/underline and Settings nav-row highlight repaint immediately after a
/// General Settings colour selection, instead of retaining the colors that happened to be present
/// when AppTheme.xaml was first parsed.
/// </summary>
public sealed class SelectedColorConverter : IValueConverter
{
    /// <summary>Resource key to use while the bound value is true, e.g. <c>AccentColor</c>.
    /// Null falls back to <see cref="SelectedColor"/> for a fixed-color use.</summary>
    public string? SelectedResourceKey { get; set; }

    /// <summary>Resource key to use while the bound value is false, e.g.
    /// <c>SecondaryTextColor</c>. Null falls back to <see cref="UnselectedColor"/>.</summary>
    public string? UnselectedResourceKey { get; set; }

    public Color SelectedColor { get; set; } = Color.FromArgb("#7C6FE0");
    public Color UnselectedColor { get; set; } = Color.FromArgb("#8A8698");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true
            ? ResolveColor(SelectedResourceKey, SelectedColor)
            : ResolveColor(UnselectedResourceKey, UnselectedColor);

    private static Color ResolveColor(string? resourceKey, Color fallback)
    {
        if (resourceKey is not null
            && Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true)
        {
            return resource switch
            {
                Color color => color,
                SolidColorBrush brush => brush.Color,
                _ => fallback,
            };
        }
        return fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringMatchColorConverter : IMultiValueConverter
{
    public string? MatchResourceKey { get; set; }
    public string? NoMatchResourceKey { get; set; }

    public Color MatchColor { get; set; } = Color.FromArgb("#7C6FE0");
    public Color NoMatchColor { get; set; } = Colors.Transparent;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isMatch = values.Length >= 2
            && values[0] is string value
            && values[1] is string comparison
            && string.Equals(value, comparison, StringComparison.Ordinal);

        return isMatch
            ? ResolveColor(MatchResourceKey, MatchColor)
            : ResolveColor(NoMatchResourceKey, NoMatchColor);
    }

    private static Color ResolveColor(string? resourceKey, Color fallback)
    {
        if (resourceKey is not null
            && Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true)
        {
            return resource switch
            {
                Color color => color,
                SolidColorBrush brush => brush.Color,
                _ => fallback,
            };
        }
        return fallback;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Picks one of two strings from a bool — e.g. the workspace form's "Edit workspace" /
/// "New workspace" title and its "Save" / "Create" button label.</summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = string.Empty;
    public string FalseText { get; set; } = string.Empty;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
