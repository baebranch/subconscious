using System.Globalization;

namespace Subconscious.Mobile;

/// <summary>Inverts Boolean values for one-way XAML visibility and enabled-state bindings.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool boolean && !boolean;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool boolean && !boolean;
}
