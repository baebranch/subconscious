using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace Subconscious.Desktop.Views;

/// <summary>User bubbles align right, assistant bubbles align left — matching the design's chat layout.</summary>
public sealed class BoolToAlignmentConverter : IValueConverter
{
    public static readonly BoolToAlignmentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>User bubbles use the lavender accent tint, assistant bubbles use the neutral gray tint.</summary>
public sealed class BoolToBubbleBrushConverter : IValueConverter
{
    public static readonly BoolToBubbleBrushConverter Instance = new();

    private static readonly IBrush UserBrush = new SolidColorBrush(Color.Parse("#EEEBFB"));
    private static readonly IBrush AssistantBrush = new SolidColorBrush(Color.Parse("#F2F2F5"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? UserBrush : AssistantBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
