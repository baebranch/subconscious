using Microsoft.Maui.Graphics;

namespace Subconscious.Chat.Debug;

internal static class ThemeResources
{
    private static readonly Color Accent = Color.FromArgb("#673AB7");

    public static void Replace(ResourceDictionary resources, bool dark)
    {
        var surface = Color.FromArgb(dark ? "#2C2C2C" : "#FFFFFF");
        var panel = surface;
        var divider = Color.FromArgb(dark ? "#454545" : "#E5E3ED");
        var primary = Color.FromArgb(dark ? "#F5F5F5" : "#1F1B2E");
        var secondary = Color.FromArgb(dark ? "#C4C4C4" : "#8A8698");
        var hover = Color.FromArgb(dark ? "#383838" : "#EFEEF4");
        var error = Color.FromArgb(dark ? "#FF8A80" : "#D9534F");
        var errorBackground = Color.FromArgb(dark ? "#4A2525" : "#FDECEA");
        var assistant = Color.FromArgb(dark ? "#333333" : "#F2F2F5");
        var user = Blend(Accent, dark ? Colors.Black : Colors.White, dark ? 0.72 : 0.88);
        var selected = Blend(Accent, panel, 0.92);

        resources["AccentColor"] = Accent;
        resources["SurfaceColor"] = surface;
        resources["PanelBackgroundColor"] = panel;
        resources["ContextRowSelectedBackgroundColor"] = selected;
        resources["DividerColor"] = divider;
        resources["PrimaryTextColor"] = primary;
        resources["SecondaryTextColor"] = secondary;
        resources["HoverColor"] = hover;
        resources["UserBubbleColor"] = user;
        resources["AssistantBubbleColor"] = assistant;
        resources["ErrorColor"] = error;
        resources["ErrorBackgroundColor"] = errorBackground;
        resources["AccentBrush"] = new SolidColorBrush(Accent);
        resources["DividerBrush"] = new SolidColorBrush(divider);
        resources["SurfaceBrush"] = new SolidColorBrush(surface);
    }

    private static Color Blend(Color color, Color other, double amount) => Color.FromRgba(
        color.Red + ((other.Red - color.Red) * amount),
        color.Green + ((other.Green - color.Green) * amount),
        color.Blue + ((other.Blue - color.Blue) * amount), 1.0);
}
