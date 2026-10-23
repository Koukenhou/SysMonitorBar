using System.Runtime.InteropServices;
using SysMonitorBar.Core.Layout;

namespace SysMonitorBar.Infrastructure;

internal static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Keeps the window out of Alt+Tab (ShowInTaskbar=False alone does not).</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    /// <summary>The window's bounds in physical pixels (virtual-screen coordinates).</summary>
    public static RectD GetWindowRectPx(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r))
        {
            return default;
        }
        return new RectD(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>Moves the window's top-left corner to a physical-pixel position; size and z-order stay.</summary>
    public static void MoveWindowPx(IntPtr hwnd, double left, double top) =>
        SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(left), (int)Math.Round(top), 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>The system DPI as a scale factor (1.0 = 96 DPI).</summary>
    public static double SystemDpiScale => GetDpiForSystem() / 96.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
