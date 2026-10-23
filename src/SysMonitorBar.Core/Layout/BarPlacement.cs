using SysMonitorBar.Core.Settings;

namespace SysMonitorBar.Core.Layout;

/// <summary>Axis-aligned rectangle in physical screen pixels.</summary>
public readonly record struct RectD(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public double IntersectionArea(RectD other)
    {
        var w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        var h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

public sealed record ScreenArea(string DeviceName, bool IsPrimary, RectD WorkArea);

/// <summary>Pure placement math for the bar, in pixels; the WPF side supplies screens and the bar size.</summary>
public static class BarPlacement
{
    /// <summary>Gap above the bar at its default position, in DIPs (scale by the monitor's DPI).</summary>
    public const double DefaultTopMargin = 16;

    /// <summary>
    /// Where to put the bar at startup: the saved top-left corner when it still lies on a
    /// connected screen (clamped into that screen's work area), otherwise the default position.
    /// </summary>
    /// <param name="dpiScale">The bar's DPI scale (1.0 = 96 DPI), used for the default top margin.</param>
    public static (double Left, double Top) Resolve(PositionSettings? saved, double width, double height,
        IReadOnlyList<ScreenArea> screens, double dpiScale = 1.0)
    {
        if (screens.Count == 0)
        {
            return (0, 0);
        }

        if (saved is { LeftPx: { } left, TopPx: { } top })
        {
            var screen = screens.FirstOrDefault(s => s.DeviceName == saved.ScreenDeviceName && s.WorkArea.Contains(left, top))
                ?? screens.FirstOrDefault(s => s.WorkArea.Contains(left, top));
            if (screen is not null)
            {
                return Clamp(left, top, width, height, screen.WorkArea);
            }
        }

        return Default(width, PrimaryOf(screens).WorkArea, dpiScale);
    }

    /// <summary>Top center of the work area, <see cref="DefaultTopMargin"/> below its top edge.</summary>
    public static (double Left, double Top) Default(double width, RectD workArea, double dpiScale = 1.0) =>
        (Math.Round(workArea.Left + Math.Max(0, (workArea.Width - width) / 2)), workArea.Top + Math.Round(DefaultTopMargin * dpiScale));

    /// <summary>Moves the rectangle the least distance needed to keep it inside <paramref name="workArea"/>.</summary>
    public static (double Left, double Top) Clamp(double left, double top, double width, double height, RectD workArea)
    {
        var x = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
        var y = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));
        return (x, y);
    }

    /// <summary>The screen showing the largest part of <paramref name="bar"/>; the primary screen when none does.</summary>
    public static ScreenArea ScreenFor(RectD bar, IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0)
        {
            throw new ArgumentException("At least one screen is required.", nameof(screens));
        }
        var best = screens.MaxBy(s => s.WorkArea.IntersectionArea(bar))!;
        return best.WorkArea.IntersectionArea(bar) > 0 ? best : PrimaryOf(screens);
    }

    private static ScreenArea PrimaryOf(IReadOnlyList<ScreenArea> screens) =>
        screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
}
