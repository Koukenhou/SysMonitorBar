using System.IO;
using SysMonitorBar.Core.Layout;
using Forms = System.Windows.Forms;

namespace SysMonitorBar.Infrastructure;

internal static class ScreenService
{
    /// <summary>
    /// Work areas of all screens in physical pixels. The process is per-monitor DPI aware
    /// (app.manifest), so these are real device pixels on every monitor.
    /// </summary>
    public static IReadOnlyList<ScreenArea> GetScreens() =>
        Forms.Screen.AllScreens
            .Select(s => new ScreenArea(
                s.DeviceName,
                s.Primary,
                new RectD(s.WorkingArea.Left, s.WorkingArea.Top, s.WorkingArea.Width, s.WorkingArea.Height)))
            .ToList();

    /// <summary>Letters of fixed drives. Reads only the drive type, which does not block on slow media.</summary>
    public static IReadOnlyList<char> GetFixedDriveLetters() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed)
            .Select(d => char.ToUpperInvariant(d.Name[0]))
            .ToList();
}
