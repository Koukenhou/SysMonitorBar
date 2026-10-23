using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Formatting;
using SysMonitorBar.Core.Layout;
using SysMonitorBar.Core.Logging;
using SysMonitorBar.Core.Resources;
using SysMonitorBar.Core.Sampling;
using SysMonitorBar.Core.Settings;
using SysMonitorBar.Infrastructure;
using SysMonitorBar.Infrastructure.Metrics;
using SysMonitorBar.ViewModels;
using SysMonitorBar.Views;

namespace SysMonitorBar;

/// <summary>
/// Owns the app's lifetime: tray, bar, settings window and sampling loop.
/// All members run on the UI thread except where noted.
/// </summary>
internal sealed class AppController
{
    private readonly SettingsService _settingsService;
    private readonly SingleInstanceGuard _instanceGuard;
    private readonly bool _simulate;
    private readonly MonitorBarViewModel _barViewModel = new();

    private AppSettings _applied;
    /// <summary>Settings currently shown on the bar: the applied ones, or a live preview from the settings window.</summary>
    private AppSettings _current;

    private TrayManager? _tray;
    private MonitorBarWindow? _bar;
    private MonitorCoordinator? _coordinator;
    private GpuProvider? _gpuProvider;
    private SettingsWindow? _settingsWindow;
    private MetricsSnapshot _latest = MetricsSnapshot.Empty;
    private MetricsSnapshot? _pendingSnapshot;
    private bool _barPlaced;
    private bool _suspended;
    private bool _exiting;

    /// <param name="simulate">Show simulated values instead of reading the hardware (<c>--simulate</c>).</param>
    public AppController(SettingsService settingsService, SingleInstanceGuard instanceGuard, AppSettings settings, bool simulate)
    {
        _settingsService = settingsService;
        _instanceGuard = instanceGuard;
        _simulate = simulate;
        _applied = settings;
        _current = settings;
    }

    public void Start(SettingsLoadResult loadResult)
    {
        // Tray first: it must exist even if everything after it fails or the bar stays hidden.
        _tray = new TrayManager(LoadTrayIcon());
        _tray.ToggleBarRequested += (_, _) => SetBarVisible(!(_bar?.IsVisible ?? false));
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.StartupToggleRequested += (_, _) => SetStartWithWindows(!_applied.Startup.StartWithWindows);
        _tray.MenuOpening += (_, _) => SyncStartWithWindows();
        _tray.ExitRequested += async (_, _) => await ExitAsync();
        if (loadResult.Status == SettingsLoadStatus.Corrupt)
        {
            _tray.ShowWarning(string.Format(CultureInfo.CurrentCulture, Strings.SettingsCorruptNotice, loadResult.CorruptBackupPath));
        }

        _applied.Display.Position.MigrateFromDip(NativeMethods.SystemDpiScale);
        RepairStartupEntry();
        SyncStartWithWindows();
        // Always leave a complete, normalized settings file behind (also after a missing or corrupt one).
        TrySave(_applied);

        _bar = new MonitorBarWindow(_barViewModel);
        _bar.DragCompleted += (_, _) => OnBarDragged();
        _bar.SizeChanged += (_, _) => KeepBarOnScreen();
        _bar.DpiChanged += (_, _) => _bar.Dispatcher.BeginInvoke(() =>
        {
            UpdateMaxWidth();
            KeepBarOnScreen();
        });

        _coordinator = new MonitorCoordinator(CreateSource(), OnSnapshotFromLoop,
            ex => Log.Error("Sampling failed.", ex));
        ShowOnBar(_current);
        _coordinator.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _instanceGuard.StartListening(() => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Log.Info("Another launch asked this instance to show the bar.");
            SetBarVisible(true);
        }));

        SetBarVisible(_applied.Display.Visible, persist: false);
    }

    private IMetricsSource CreateSource()
    {
        if (_simulate)
        {
            Log.Info("Using simulated data (--simulate).");
            return new SimulatedMetricsSource();
        }

        _gpuProvider = new GpuProvider { PreferredId = _current.Display.GpuDeviceId };
        return new CompositeMetricsSource(
            [new CpuProvider(), _gpuProvider, new MemoryProvider(), new DiskProvider()],
            onProviderError: (name, ex) => Log.Warning($"{name} source failed; retrying later.", ex),
            onProviderRecovered: name => Log.Info($"{name} source recovered."));
    }

    public async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        Log.Info("Exiting.");

        // Order matters: stop sampling, then release the sources, then the tray, then shut down.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _settingsWindow?.Close();
        if (_coordinator is not null)
        {
            await _coordinator.DisposeAsync();
        }
        _tray?.Dispose();
        if (_bar is not null)
        {
            _bar.AllowClose = true;
            _bar.Close();
        }
        _instanceGuard.Dispose();
        Application.Current.Shutdown();
    }

    private void SetBarVisible(bool visible, bool persist = true)
    {
        if (_bar is null || _exiting)
        {
            return;
        }

        if (visible)
        {
            _bar.Show();
            if (!_barPlaced)
            {
                PlaceBarAtStartup();
            }
        }
        else
        {
            _bar.Hide();
        }
        UpdateSampling();

        _tray?.SetBarVisible(visible);
        if (persist && _applied.Display.Visible != visible)
        {
            _applied.Display.Visible = visible;
            _current.Display.Visible = visible;
            TrySave(_applied);
        }
    }

    /// <summary>Samples only while it is useful: not during sleep, and not while hidden if the user chose so.</summary>
    private void UpdateSampling()
    {
        var hidden = _bar is not { IsVisible: true };
        if (_suspended || (hidden && _current.Sampling.PauseWhenHidden))
        {
            _coordinator?.Pause();
        }
        else
        {
            _coordinator?.Resume();
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        SyncStartWithWindows();
        var viewModel = new SettingsViewModel(_applied, ScreenService.GetFixedDriveLetters(), DxgiAdapters.TryEnumerate(), ShowOnBar);
        _settingsWindow = new SettingsWindow(viewModel, ApplySettings);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ShowOnBar(_applied); // drop any unapplied preview
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private bool ApplySettings(AppSettings edited)
    {
        // The settings window does not edit position or visibility; keep the live values.
        edited = edited.Clone();
        edited.Display.Position = _applied.Display.Position;
        edited.Display.Visible = _applied.Display.Visible;

        if (edited.Startup.StartWithWindows != _applied.Startup.StartWithWindows &&
            !SetStartWithWindows(edited.Startup.StartWithWindows))
        {
            return false;
        }
        if (!TrySave(edited))
        {
            return false;
        }
        _applied = edited;
        ShowOnBar(_applied);
        Log.Info($"Settings applied: metrics [{string.Join(", ", _applied.Display.MetricIds)}], " +
                 $"interval {_applied.Sampling.IntervalMs} ms, GPU {_applied.Display.GpuDeviceId ?? "auto"}.");
        return true;
    }

    /// <summary>Pushes settings to the bar and the sampling loop without saving them.</summary>
    private void ShowOnBar(AppSettings settings)
    {
        _current = settings.Clone();
        _barViewModel.ApplyAppearance(_current.Display);
        if (_gpuProvider is not null)
        {
            _gpuProvider.PreferredId = _current.Display.GpuDeviceId;
        }
        _coordinator?.Configure(_current.GetMetricKeys(), TimeSpan.FromMilliseconds(_current.Sampling.IntervalMs));
        UpdateSampling();
        UpdateMaxWidth();
        Render();
    }

    /// <summary>Called on the sampling thread. Coalesces to the latest snapshot; at most one dispatch is pending.</summary>
    private void OnSnapshotFromLoop(MetricsSnapshot snapshot)
    {
        if (Interlocked.Exchange(ref _pendingSnapshot, snapshot) is null)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var latest = Interlocked.Exchange(ref _pendingSnapshot, null);
                if (latest is not null && !_exiting)
                {
                    _latest = latest;
                    Render();
                }
            });
        }
    }

    private void Render()
    {
        var keys = _current.GetMetricKeys();
        var text = MetricFormatter.Format(keys, _latest, _current.Display.ShowSeparator);
        _barViewModel.RenderedText = text;
        _barViewModel.ToolTipText = MetricFormatter.BuildTooltip(keys, _latest, text);
    }

    // ---- Start with Windows ---------------------------------------------------------------

    /// <summary>The registry is the source of truth; mirror it into the settings and the tray menu.</summary>
    private void SyncStartWithWindows()
    {
        bool enabled;
        try
        {
            enabled = StartupRegistration.Read().IsEnabled;
        }
        catch (Exception ex) when (StartupRegistration.IsRegistryError(ex))
        {
            Log.Warning("Could not read the startup entry.", ex);
            return;
        }

        _tray?.SetStartWithWindows(enabled);
        if (_applied.Startup.StartWithWindows != enabled)
        {
            _applied.Startup.StartWithWindows = enabled;
            _current.Startup.StartWithWindows = enabled;
            TrySave(_applied);
        }
    }

    private bool SetStartWithWindows(bool enable)
    {
        try
        {
            if (enable)
            {
                StartupRegistration.Enable();
            }
            else
            {
                StartupRegistration.Disable();
            }
            Log.Info(enable ? $"Start with Windows enabled: {StartupRegistration.CurrentExePath}" : "Start with Windows disabled.");
        }
        catch (Exception ex) when (StartupRegistration.IsRegistryError(ex))
        {
            Log.Warning("Could not change the startup entry.", ex);
            _tray?.ShowWarning(string.Format(CultureInfo.CurrentCulture, Strings.StartupChangeFailed, ex.Message));
            return false;
        }
        SyncStartWithWindows();
        return true;
    }

    private static void RepairStartupEntry()
    {
        try
        {
            if (StartupRegistration.RepairIfMoved())
            {
                Log.Info($"Startup entry pointed at a missing exe; updated to {StartupRegistration.CurrentExePath}.");
            }
        }
        catch (Exception ex) when (StartupRegistration.IsRegistryError(ex))
        {
            Log.Warning("Could not repair the startup entry.", ex);
        }
    }

    // ---- Placement (physical pixels; the process is per-monitor DPI aware) ----------------

    private void PlaceBarAtStartup()
    {
        if (_bar is null)
        {
            return;
        }
        _bar.UpdateLayout();
        var size = BarRect();
        var (left, top) = BarPlacement.Resolve(_applied.Display.Position, size.Width, size.Height,
            ScreenService.GetScreens(), BarDpiScale());
        NativeMethods.MoveWindowPx(_bar.Handle, left, top);
        _barPlaced = true;
        UpdateMaxWidth();
        KeepBarOnScreen(); // the move may have changed the DPI and therefore the size
    }

    private void OnBarDragged()
    {
        if (_bar is null)
        {
            return;
        }
        KeepBarOnScreen();
        var rect = BarRect();
        var screen = BarPlacement.ScreenFor(rect, ScreenService.GetScreens());
        var position = new PositionSettings
        {
            ScreenDeviceName = screen.DeviceName,
            LeftPx = (int)rect.Left,
            TopPx = (int)rect.Top,
        };
        _applied.Display.Position = position;
        _current.Display.Position = position;
        UpdateMaxWidth();
        TrySave(_applied);
    }

    /// <summary>Clamps the bar into the work area of the screen it is mostly on (it may grow past the edge).</summary>
    private void KeepBarOnScreen()
    {
        if (_bar is not { IsVisible: true } || !_barPlaced)
        {
            return;
        }
        var rect = BarRect();
        var screen = BarPlacement.ScreenFor(rect, ScreenService.GetScreens());
        var (left, top) = BarPlacement.Clamp(rect.Left, rect.Top, rect.Width, rect.Height, screen.WorkArea);
        if (left != rect.Left || top != rect.Top)
        {
            NativeMethods.MoveWindowPx(_bar.Handle, left, top);
        }
    }

    /// <summary>Max bar width: a share of its monitor's work area, converted to the bar's DIPs.</summary>
    private void UpdateMaxWidth()
    {
        if (_bar is null)
        {
            return;
        }
        var screens = ScreenService.GetScreens();
        if (screens.Count == 0)
        {
            return;
        }
        var screen = _barPlaced ? BarPlacement.ScreenFor(BarRect(), screens) : screens.First(s => s.IsPrimary);
        _barViewModel.MaxBarWidth = Math.Floor(screen.WorkArea.Width * _current.Display.MaxWidthRatio / BarDpiScale());
    }

    private double BarDpiScale() => _bar is null ? 1.0 : VisualTreeHelper.GetDpi(_bar).DpiScaleX;

    private RectD BarRect() => _bar is null ? default : NativeMethods.GetWindowRectPx(_bar.Handle);

    // ---- System events (raised on a system thread) ----------------------------------------

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Log.Info("Display settings changed.");
            UpdateMaxWidth();
            KeepBarOnScreen();
        });

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    Log.Info("Suspending: sampling paused.");
                    _suspended = true;
                    UpdateSampling();
                    break;
                case PowerModes.Resume:
                    Log.Info("Resumed: resetting sources.");
                    _suspended = false;
                    // Counter queries and adapter LUIDs may be stale after sleep (GPU drivers can reset).
                    _coordinator?.RequestReset();
                    UpdateSampling();
                    UpdateMaxWidth();
                    KeepBarOnScreen();
                    break;
            }
        });

    private bool TrySave(AppSettings settings)
    {
        try
        {
            _settingsService.Save(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Could not save settings.", ex);
            _tray?.ShowWarning(string.Format(CultureInfo.CurrentCulture, Strings.SettingsSaveFailed, ex.Message));
            return false;
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SysMonitorBar.ico"))
            ?? throw new InvalidOperationException("Tray icon resource is missing.");
        using var stream = info.Stream;
        return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }
}
