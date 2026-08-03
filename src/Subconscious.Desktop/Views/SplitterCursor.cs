namespace Subconscious.Desktop.Views;

/// <summary>
/// Gives a divider the west-east resize cursor. MAUI has no cross-platform cursor API, and WinUI's
/// <c>UIElement.ProtectedCursor</c> is protected (it's meant to be set from a subclass), so it's
/// reached by reflection on the platform view - the alternative would be a custom handler for what
/// is otherwise a plain Grid.
/// </summary>
internal static class SplitterCursor
{
    public static void ApplyResizeCursor(View splitter)
    {
#if WINDOWS
        // The platform view doesn't exist yet when this is called from a constructor, so also
        // react to the handler being created.
        splitter.HandlerChanged += (_, _) => TrySetResizeCursor(splitter);
        TrySetResizeCursor(splitter);
#endif
    }

#if WINDOWS
    private static void TrySetResizeCursor(View splitter)
    {
        if (splitter.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            return;
        }

        try
        {
            var property = typeof(Microsoft.UI.Xaml.UIElement).GetProperty(
                "ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            property?.SetValue(
                element,
                Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast));
        }
        catch (Exception)
        {
            // Cursor feedback is a nicety - the hover highlight still marks the grab area.
        }
    }
#endif
}
