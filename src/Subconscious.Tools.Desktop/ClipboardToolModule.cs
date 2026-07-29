using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using System.ComponentModel;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// Clipboard operations tool module. Provides read/write access to the system clipboard.
/// Port of Python's <c>desktop_tools/clipboard.py</c>.
/// </summary>
public sealed class ClipboardToolModule : IToolModule
{
    public string Slug => "clipboard";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                GetText,
                "get_clipboard_text",
                "Get the current text content from the clipboard."),

            AIFunctionFactory.Create(
                SetText,
                "set_clipboard_text",
                "Set text content to the clipboard.")
        ];
    }

    private static string GetText(EngineContext context)
    {
#if WINDOWS
        try
        {
            var text = System.Windows.Forms.Clipboard.GetText();
            return string.IsNullOrEmpty(text) ? "Clipboard is empty" : text;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "Error: Unable to access clipboard (requires a UI session)";
        }
        catch (Exception ex)
        {
            return $"Error reading clipboard: {ex.Message}";
        }
#else
        return "Error: Clipboard access is only supported on Windows in this build.";
#endif
    }

    private static string SetText(
        [Description("The text to copy to the clipboard.")] string text,
        EngineContext context)
    {
#if WINDOWS
        try
        {
            System.Windows.Forms.Clipboard.SetText(text ?? string.Empty);
            return $"Successfully copied {text?.Length ?? 0} characters to clipboard";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "Error: Unable to access clipboard (requires a UI session)";
        }
        catch (Exception ex)
        {
            return $"Error writing to clipboard: {ex.Message}";
        }
#else
        return "Error: Clipboard access is only supported on Windows in this build.";
#endif
    }
}
