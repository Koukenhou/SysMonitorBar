using SysMonitorBar.Core.Layout;
using SysMonitorBar.Core.Settings;

namespace SysMonitorBar.Tests;

public class BarPlacementTests
{
    // Primary 1600x900 (taskbar 40 high) and a 1920x1080 screen to its right, as on the dev PC.
    private static readonly ScreenArea Primary = new(@"\\.\DISPLAY1", true, new RectD(0, 0, 1600, 860));
    private static readonly ScreenArea Second = new(@"\\.\DISPLAY2", false, new RectD(1600, 0, 1920, 1040));
    private static readonly IReadOnlyList<ScreenArea> Screens = [Primary, Second];

    [Fact]
    public void Resolve_NoSavedPositionUsesTopCenterOfPrimary()
    {
        var (left, top) = BarPlacement.Resolve(new PositionSettings(), 400, 30, Screens);

        Assert.Equal(600, left);
        Assert.Equal(BarPlacement.DefaultTopMargin, top);
    }

    [Fact]
    public void Resolve_DefaultTopMarginScalesWithDpi()
    {
        Assert.Equal((600d, 24d), BarPlacement.Resolve(new PositionSettings(), 400, 30, Screens, dpiScale: 1.5));
    }

    [Fact]
    public void Resolve_KeepsSavedPositionOnSecondScreen()
    {
        var saved = new PositionSettings { ScreenDeviceName = Second.DeviceName, LeftPx = 2000, TopPx = 100 };

        Assert.Equal((2000d, 100d), BarPlacement.Resolve(saved, 400, 30, Screens));
    }

    [Fact]
    public void Resolve_ClampsBarThatWouldOverflowTheRightEdge()
    {
        var saved = new PositionSettings { ScreenDeviceName = Second.DeviceName, LeftPx = 3400, TopPx = 1030 };

        Assert.Equal((3120d, 1010d), BarPlacement.Resolve(saved, 400, 30, Screens));
    }

    [Fact]
    public void Resolve_DisconnectedScreenFallsBackToDefault()
    {
        var saved = new PositionSettings { ScreenDeviceName = Second.DeviceName, LeftPx = 2000, TopPx = 100 };

        Assert.Equal((600d, 16d), BarPlacement.Resolve(saved, 400, 30, [Primary]));
    }

    [Fact]
    public void ScreenFor_PicksScreenWithLargestOverlap()
    {
        Assert.Same(Second, BarPlacement.ScreenFor(new RectD(1500, 10, 400, 30), Screens));
        Assert.Same(Primary, BarPlacement.ScreenFor(new RectD(1300, 10, 400, 30), Screens));
        Assert.Same(Primary, BarPlacement.ScreenFor(new RectD(-5000, -5000, 400, 30), Screens));
    }

    [Fact]
    public void Clamp_BarWiderThanWorkAreaSticksToLeftEdge()
    {
        Assert.Equal((0d, 0d), BarPlacement.Clamp(-50, -10, 2000, 30, Primary.WorkArea));
    }
}
