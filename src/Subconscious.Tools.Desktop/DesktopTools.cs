using Subconscious.Engine.Tools;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// Desktop-specific tool registry extending the base tool registry with platform-dependent tools.
/// Port of <c>desktop_tools/</c> from the Python implementation.
/// <para>
/// Desktop tools include: filesystem operations, terminal commands, clipboard access,
/// image processing, system settings, and automation capabilities.
/// </para>
/// </summary>
public sealed class DesktopTools : BaseToolRegistry
{
    public DesktopTools()
    {
        LoadDesktopTools();
    }

    /// <summary>
    /// Register desktop-specific tool modules.
    /// </summary>
    private void LoadDesktopTools()
    {
        Register(new FilesystemToolModule());
        Register(new TerminalToolModule());
        Register(new ClipboardToolModule());
        Register(new ImageToolModule());
        Register(new SettingsToolModule());
        Register(new AutomationToolModule());
        Register(new WebToolModule());
    }
}
