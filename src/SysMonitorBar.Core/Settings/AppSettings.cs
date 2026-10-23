using System.Text.Json;
using System.Text.Json.Serialization;
using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Core.Settings;

/// <summary>Persisted settings (<c>settings.json</c>). See docs/DesignChanges.md for the differences from design v1.0 §9.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DisplaySettings Display { get; set; } = new();
    public SamplingSettings Sampling { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();

    public static AppSettings CreateDefault() => new();

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, SettingsJson.Options), SettingsJson.Options)!;

    /// <summary>Parsed <see cref="DisplaySettings.MetricIds"/>, in stored order; unparsable ids are skipped.</summary>
    public IReadOnlyList<MetricKey> GetMetricKeys()
    {
        var keys = new List<MetricKey>();
        foreach (var id in Display.MetricIds)
        {
            if (MetricKey.TryParse(id, out var key) && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }
        return keys;
    }
}

public sealed class DisplaySettings
{
    public const double MinBackgroundOpacity = 0.2;
    public const double MaxBackgroundOpacity = 1.0;
    public const double MinFontSize = 10;
    public const double MaxFontSize = 24;
    public const double MinMaxWidthRatio = 0.3;
    public const double MaxMaxWidthRatio = 1.0;

    public static IReadOnlyList<string> DefaultMetricIds { get; } =
    [
        "cpu.usage",
        "gpu.usage",
        "memory.usedTotal",
        "gpu.vramUsedTotal",
        "disk.C.usedPercent",
    ];

    public bool Visible { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Opacity of the bar background only (0.2-1.0); text always stays opaque.</summary>
    public double BackgroundOpacity { get; set; } = 0.8;

    public double FontSize { get; set; } = 13;
    public bool ShowSeparator { get; set; } = true;
    public double MaxWidthRatio { get; set; } = 0.9;

    /// <summary>Display order is the list order.</summary>
    public List<string> MetricIds { get; set; } = [.. DefaultMetricIds];

    public string? GpuDeviceId { get; set; }
    public PositionSettings Position { get; set; } = new();
}

/// <summary>
/// Top-left corner of the bar in physical screen pixels (virtual-screen coordinates), plus the
/// monitor it was on. Pixels, not DIPs: with per-monitor DPI a DIP means a different size on
/// each monitor, while pixels compare directly with the monitors' work areas.
/// </summary>
public sealed class PositionSettings
{
    public string? ScreenDeviceName { get; set; }
    public int? LeftPx { get; set; }
    public int? TopPx { get; set; }

    /// <summary>Stage A format (system-DPI DIPs). Read once for migration, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LeftDip { get; set; }

    /// <inheritdoc cref="LeftDip"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopDip { get; set; }

    /// <summary>Converts a stage A position (DIPs at the system DPI) to pixels and clears the old fields.</summary>
    public void MigrateFromDip(double systemScale)
    {
        if (LeftPx is null && TopPx is null && LeftDip is { } left && TopDip is { } top &&
            double.IsFinite(left) && double.IsFinite(top))
        {
            LeftPx = (int)Math.Round(left * systemScale);
            TopPx = (int)Math.Round(top * systemScale);
        }
        LeftDip = null;
        TopDip = null;
    }
}

public sealed class SamplingSettings
{
    public const int DefaultIntervalMs = 1000;
    public static IReadOnlyList<int> AllowedIntervalsMs { get; } = [500, 1000, 2000, 5000];

    public int IntervalMs { get; set; } = DefaultIntervalMs;
    public bool PauseWhenHidden { get; set; } = true;
}

public sealed class StartupSettings
{
    public bool StartWithWindows { get; set; }
}
