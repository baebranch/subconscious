#if WINDOWS

using System.Windows.Forms;
using Subconscious.Engine.Tray;

namespace Subconscious.Desktop.Tray;

/// <summary>
/// Windows tray icon backed by <see cref="System.Windows.Forms.NotifyIcon"/>.
///
/// This is the .NET analog of <c>desktop/tray.py</c>'s use of <c>pystray</c>: a
/// long-running notification-area icon with a context menu so the engine can keep
/// running in the background after the main window closes. Requires a Win32 message
/// loop to pump events — <see cref="Run"/> owns that (via a hidden
/// <see cref="ApplicationContext"/> and <see cref="Application.Run(ApplicationContext)"/>),
/// so it must be called on a dedicated STA thread (see
/// <see cref="TrayIconServiceFactory.RunOnStaThread"/>).
/// </summary>
public sealed class WindowsTrayIconService : ITrayIconService
{
    private NotifyIcon? _notifyIcon;
    private ApplicationContext? _appContext;
    private readonly object _sync = new();

    public bool IsSupported => true;

    /// <summary>
    /// Pumps the Win32 message loop for this tray icon. Blocks until <see cref="Dispose"/>
    /// or <see cref="Hide"/> is called (via <see cref="ApplicationContext.ExitThread"/>).
    /// Must be called from the same STA thread that will call <see cref="Show"/>.
    /// </summary>
    public void Run()
    {
        _appContext = new ApplicationContext();
        Application.Run(_appContext);
    }

    public void Show(string tooltip, string iconPath, IReadOnlyList<TrayMenuItem> menuItems)
    {
        lock (_sync)
        {
            _notifyIcon?.Dispose();

            var contextMenu = new ContextMenuStrip();
            TrayMenuItem? defaultItem = null;
            foreach (var item in menuItems)
            {
                var menuEntry = new ToolStripMenuItem(item.Label);
                menuEntry.Click += (_, _) => item.OnClick();
                contextMenu.Items.Add(menuEntry);
                if (item.IsDefault)
                {
                    defaultItem = item;
                }
            }

            _notifyIcon = new NotifyIcon
            {
                Text = tooltip,
                Icon = File.Exists(iconPath)
                    ? new System.Drawing.Icon(iconPath)
                    : System.Drawing.SystemIcons.Application,
                ContextMenuStrip = contextMenu,
                Visible = true,
            };

            // Mirrors pystray's `default=True` menu item: a plain double-click on the
            // icon invokes it directly, without opening the context menu first.
            if (defaultItem is { } captured)
            {
                _notifyIcon.DoubleClick += (_, _) => captured.OnClick();
            }
        }
    }

    public void Hide()
    {
        lock (_sync)
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            _appContext?.ExitThread();
        }
    }

    public void Dispose()
    {
        Hide();
        _appContext?.Dispose();
    }
}

#endif
