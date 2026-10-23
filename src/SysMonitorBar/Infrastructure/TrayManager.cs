using System.Drawing;
using System.Windows.Forms;
using SysMonitorBar.Core.Resources;

namespace SysMonitorBar.Infrastructure;

/// <summary>
/// The notification-area icon and its menu. Created first at startup so the app always has a
/// control entry, even when the bar is hidden. Events are raised on the UI thread.
/// </summary>
internal sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _showBarItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Icon _icon;

    public TrayManager(Icon icon)
    {
        _icon = icon;

        var header = new ToolStripMenuItem(Strings.AppName) { Enabled = false };
        header.Font = new Font(header.Font, System.Drawing.FontStyle.Bold);
        _showBarItem = new ToolStripMenuItem(Strings.TrayShowBar);
        _showBarItem.Click += (_, _) => ToggleBarRequested?.Invoke(this, EventArgs.Empty);
        var settingsItem = new ToolStripMenuItem(Strings.TraySettings);
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _startupItem = new ToolStripMenuItem(Strings.TrayStartWithWindows);
        _startupItem.Click += (_, _) => StartupToggleRequested?.Invoke(this, EventArgs.Empty);
        var exitItem = new ToolStripMenuItem(Strings.TrayExit);
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        // Lets the owner refresh check marks (the startup entry can change outside the app).
        menu.Opening += (_, _) => MenuOpening?.Invoke(this, EventArgs.Empty);
        menu.Items.AddRange(
        [
            header,
            new ToolStripSeparator(),
            _showBarItem,
            settingsItem,
            _startupItem,
            new ToolStripSeparator(),
            exitItem,
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = Strings.AppName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleBarRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ToggleBarRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? StartupToggleRequested;
    public event EventHandler? MenuOpening;

    public void SetBarVisible(bool visible) => _showBarItem.Checked = visible;

    public void SetStartWithWindows(bool enabled) => _startupItem.Checked = enabled;

    public void ShowWarning(string message) =>
        _notifyIcon.ShowBalloonTip(8000, Strings.AppName, message, ToolTipIcon.Warning);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
