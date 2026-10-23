using System.Globalization;
using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Gpu;
using SysMonitorBar.Core.Resources;
using SysMonitorBar.Core.Settings;

namespace SysMonitorBar.ViewModels;

internal sealed class MetricOptionViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isChecked;

    public MetricOptionViewModel(MetricKey key, string label, bool isAvailable, Action changed)
    {
        Key = key;
        Label = isAvailable ? label : label + Strings.UnavailableSuffix;
        IsAvailable = isAvailable;
        _changed = changed;
    }

    public MetricKey Key { get; }
    public string Label { get; }
    public bool IsAvailable { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetField(ref _isChecked, value))
            {
                _changed();
            }
        }
    }

    /// <summary>Sets the value without triggering a preview.</summary>
    public void Load(bool isChecked)
    {
        _isChecked = isChecked;
        OnPropertyChanged(nameof(IsChecked));
    }
}

internal sealed record MetricGroupViewModel(string Header, IReadOnlyList<MetricOptionViewModel> Items, string? EmptyText = null)
{
    public bool IsEmpty => Items.Count == 0;
}

internal sealed record IntervalOption(int Milliseconds, string Label);

/// <summary>A choice in the GPU drop-down; <see cref="Id"/> is null for automatic selection.</summary>
internal sealed record GpuOption(string? Id, string Label);

/// <summary>
/// Editable copy of the settings shown in the settings window. Every edit is pushed to the bar
/// as a live preview; nothing is written to disk until the window applies it.
/// </summary>
internal sealed class SettingsViewModel : ObservableObject
{
    private readonly Action<AppSettings> _preview;
    private AppSettings _applied;
    private bool _loading;

    private int _opacityPercent;
    private double _fontSize;
    private bool _alwaysOnTop;
    private bool _showSeparator;
    private IntervalOption _selectedInterval;
    private bool _pauseWhenHidden;
    private bool _startWithWindows;
    private GpuOption _selectedGpu;
    private bool _isDirty;

    public SettingsViewModel(AppSettings applied, IReadOnlyCollection<char> fixedDrives,
        IReadOnlyList<GpuAdapterInfo> gpus, Action<AppSettings> preview)
    {
        _applied = applied.Clone();
        _preview = preview;

        GpuOptions = BuildGpuOptions(gpus, applied.Display.GpuDeviceId);
        HasGpu = gpus.Any(g => !g.IsSoftware);
        _selectedGpu = GpuOptions[0];

        Intervals = SamplingSettings.AllowedIntervalsMs
            .Select(ms => new IntervalOption(ms, string.Format(CultureInfo.InvariantCulture, Strings.IntervalSecondsFormat, ms / 1000d)))
            .ToList();
        _selectedInterval = Intervals[0];

        var savedDrives = applied.GetMetricKeys().Where(k => k.IsDisk).Select(k => k.Drive!.Value);
        var drives = fixedDrives.Select(char.ToUpperInvariant).Union(savedDrives).Order().ToList();
        Groups =
        [
            new(Strings.GroupCpu, [Option(MetricKind.CpuUsage, Strings.MetricCpuUsage)]),
            new(Strings.GroupGpu,
            [
                Option(MetricKind.GpuUsage, Strings.MetricGpuUsage),
                Option(MetricKind.VramUsedTotal, Strings.MetricVramUsedTotal),
                Option(MetricKind.VramPercent, Strings.MetricVramPercent),
            ]),
            new(Strings.GroupMemory,
            [
                Option(MetricKind.MemoryUsedTotal, Strings.MetricMemoryUsedTotal),
                Option(MetricKind.MemoryPercent, Strings.MetricMemoryPercent),
            ]),
            new(Strings.GroupDisk,
                drives.SelectMany(d => new[]
                {
                    DiskOption(MetricKind.DiskUsedPercent, d, Strings.MetricDiskUsedPercent, fixedDrives),
                    DiskOption(MetricKind.DiskFree, d, Strings.MetricDiskFree, fixedDrives),
                }).ToList(),
                Strings.NoFixedDrives),
        ];

        LoadFrom(applied, preview: false);
    }

    public IReadOnlyList<MetricGroupViewModel> Groups { get; }
    public IReadOnlyList<IntervalOption> Intervals { get; }
    public IReadOnlyList<GpuOption> GpuOptions { get; }
    public bool HasGpu { get; }

    public GpuOption SelectedGpu
    {
        get => _selectedGpu;
        set => SetAndPreview(ref _selectedGpu, value);
    }
    public IReadOnlyList<double> FontSizes { get; } =
        Enumerable.Range((int)DisplaySettings.MinFontSize, (int)(DisplaySettings.MaxFontSize - DisplaySettings.MinFontSize) + 1)
            .Select(i => (double)i)
            .ToList();

    public int MinOpacityPercent => (int)(DisplaySettings.MinBackgroundOpacity * 100);
    public int MaxOpacityPercent => (int)(DisplaySettings.MaxBackgroundOpacity * 100);

    public int OpacityPercent
    {
        get => _opacityPercent;
        set => SetAndPreview(ref _opacityPercent, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetAndPreview(ref _fontSize, value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set => SetAndPreview(ref _alwaysOnTop, value);
    }

    public bool ShowSeparator
    {
        get => _showSeparator;
        set => SetAndPreview(ref _showSeparator, value);
    }

    public IntervalOption SelectedInterval
    {
        get => _selectedInterval;
        set
        {
            if (SetAndPreview(ref _selectedInterval, value))
            {
                OnPropertyChanged(nameof(ShowFastIntervalWarning));
            }
        }
    }

    public bool ShowFastIntervalWarning => SelectedInterval.Milliseconds < SamplingSettings.DefaultIntervalMs;

    public bool PauseWhenHidden
    {
        get => _pauseWhenHidden;
        set => SetAndPreview(ref _pauseWhenHidden, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetAndPreview(ref _startWithWindows, value);
    }

    /// <summary>True when the edited values differ from the last applied ones.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetField(ref _isDirty, value);
    }

    /// <summary>The edited settings on top of the applied ones (position and visibility are kept).</summary>
    public AppSettings ToSettings()
    {
        var s = _applied.Clone();
        var d = s.Display;
        d.BackgroundOpacity = OpacityPercent / 100d;
        d.FontSize = FontSize;
        d.AlwaysOnTop = AlwaysOnTop;
        d.ShowSeparator = ShowSeparator;
        d.GpuDeviceId = SelectedGpu.Id;
        d.MetricIds =Groups.SelectMany(g => g.Items)
            .Where(i => i.IsChecked)
            .Select(i => i.Key)
            .Order()
            .Select(k => k.Id)
            .ToList();
        s.Sampling.IntervalMs = SelectedInterval.Milliseconds;
        s.Sampling.PauseWhenHidden = PauseWhenHidden;
        s.Startup.StartWithWindows = StartWithWindows;
        return SettingsService.Normalize(s);
    }

    public void RestoreDefaults() => LoadFrom(AppSettings.CreateDefault(), preview: true);

    public void MarkApplied(AppSettings applied)
    {
        _applied = applied.Clone();
        IsDirty = false;
    }

    private void LoadFrom(AppSettings s, bool preview)
    {
        _loading = true;
        try
        {
            var d = s.Display;
            OpacityPercent = (int)Math.Round(d.BackgroundOpacity * 100);
            FontSize = d.FontSize;
            AlwaysOnTop = d.AlwaysOnTop;
            ShowSeparator = d.ShowSeparator;
            SelectedInterval = Intervals.FirstOrDefault(i => i.Milliseconds == s.Sampling.IntervalMs) ?? Intervals[1];
            PauseWhenHidden = s.Sampling.PauseWhenHidden;
            StartWithWindows = s.Startup.StartWithWindows;
            SelectedGpu = GpuOptions.FirstOrDefault(g => g.Id == d.GpuDeviceId) ?? GpuOptions[0];
            var keys = s.GetMetricKeys();
            foreach (var item in Groups.SelectMany(g => g.Items))
            {
                item.Load(keys.Contains(item.Key));
            }
        }
        finally
        {
            _loading = false;
        }
        Changed(preview);
    }

    private static List<GpuOption> BuildGpuOptions(IReadOnlyList<GpuAdapterInfo> gpus, string? savedId)
    {
        var hardware = gpus.Where(g => !g.IsSoftware).ToList();
        var auto = GpuSelector.Select(hardware, null).Adapter;
        var options = new List<GpuOption>
        {
            new(null, auto is null
                ? Strings.GpuAuto
                : string.Format(CultureInfo.CurrentCulture, Strings.GpuAutoWithName, auto.Name)),
        };
        options.AddRange(hardware.Select(g => new GpuOption(g.Id, g.Name)));

        // Keep a saved choice that is not connected right now instead of silently dropping it.
        if (!string.IsNullOrEmpty(savedId) && hardware.All(g => g.Id != savedId))
        {
            options.Add(new GpuOption(savedId, savedId + Strings.UnavailableSuffix));
        }
        return options;
    }

    private MetricOptionViewModel Option(MetricKind kind, string label) =>
        new(MetricKey.Of(kind), label, isAvailable: true, () => Changed(preview: true));

    private MetricOptionViewModel DiskOption(MetricKind kind, char drive, string format, IReadOnlyCollection<char> fixedDrives) =>
        new(MetricKey.Disk(kind, drive), string.Format(CultureInfo.InvariantCulture, format, drive),
            fixedDrives.Contains(drive), () => Changed(preview: true));

    private bool SetAndPreview<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetField(ref field, value, name))
        {
            return false;
        }
        Changed(preview: true);
        return true;
    }

    private void Changed(bool preview)
    {
        if (_loading)
        {
            return;
        }
        var edited = ToSettings();
        IsDirty = !SettingsEquals(edited, _applied);
        if (preview)
        {
            _preview(edited);
        }
    }

    private static bool SettingsEquals(AppSettings a, AppSettings b) =>
        System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
}
