namespace Subconscious.Desktop.Views;

/// <summary>
/// Pointer capture for drag interactions. MAUI's <see cref="PointerGestureRecognizer"/> has no
/// cross-platform capture concept, so this reaches the native event through
/// <c>PointerEventArgs.PlatformArgs</c> on Windows and does nothing elsewhere - the divider drag
/// still works without capture, it just stops tracking if the pointer leaves the app's panels.
/// </summary>
internal static class PointerCapture
{
    public static void Capture(PointerEventArgs e, View sender)
    {
#if WINDOWS
        try
        {
            if (e.PlatformArgs?.PointerRoutedEventArgs is { } args
                && sender.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
            {
                element.CapturePointer(args.Pointer);
            }
        }
        catch (Exception)
        {
            // Capture is an optimisation, not a requirement.
        }
#endif
    }

    public static void Release(PointerEventArgs e, View sender)
    {
#if WINDOWS
        try
        {
            if (e.PlatformArgs?.PointerRoutedEventArgs is { } args
                && sender.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
            {
                element.ReleasePointerCapture(args.Pointer);
            }
        }
        catch (Exception)
        {
            // As above.
        }
#endif
    }
}
