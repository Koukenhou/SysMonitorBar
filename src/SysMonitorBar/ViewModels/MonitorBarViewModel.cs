using SysMonitorBar.Core.Settings;

namespace SysMonitorBar.ViewModels;

internal sealed class MonitorBarViewModel : ObservableObject
{
    private string _renderedText = string.Empty;
    private string _toolTipText = string.Empty;
    private double _backgroundOpacity = 0.8;
    private double _fontSize = 13;
    private bool _alwaysOnTop = true;
    private double _maxBarWidth = double.PositiveInfinity;

    public string RenderedText
    {
        get => _renderedText;
        set => SetField(ref _renderedText, value);
    }

    public string ToolTipText
    {
        get => _toolTipText;
        set => SetField(ref _toolTipText, value);
    }

    /// <summary>Applied to the background brush only, never to the window or the text.</summary>
    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set => SetField(ref _backgroundOpacity, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetField(ref _fontSize, value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetField(ref _alwaysOnTop, value);
    }

    public double MaxBarWidth
    {
        get => _maxBarWidth;
        set => SetField(ref _maxBarWidth, value);
    }

    public void ApplyAppearance(DisplaySettings display)
    {
        BackgroundOpacity = display.BackgroundOpacity;
        FontSize = display.FontSize;
        AlwaysOnTop = display.AlwaysOnTop;
    }
}
