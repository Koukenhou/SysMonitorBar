using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SysMonitorBar.Infrastructure;
using SysMonitorBar.ViewModels;

namespace SysMonitorBar.Views;

internal partial class MonitorBarWindow : Window
{
    public MonitorBarWindow(MonitorBarViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        // Off-screen until the owner places it, so it never flashes at a default spot.
        Left = -32000;
        Top = -32000;
        SourceInitialized += (_, _) => NativeMethods.MakeToolWindow(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Raised once after each completed drag, with the final position.</summary>
    public event EventHandler? DragCompleted;

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>When false (the default), closing only hides the window.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            // e.g. Alt+F4: hiding is handled by the tray menu; never close the bar implicitly.
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        var before = (Left, Top);
        DragMove(); // returns when the button is released
        if ((Left, Top) != before)
        {
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
