using System.Globalization;
using System.Text;
using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Resources;

namespace SysMonitorBar.Core.Formatting;

/// <summary>Builds the single-line bar text from a snapshot. No WPF dependency.</summary>
public static class MetricFormatter
{
    public const string Separator = " | ";
    public const string NoSeparator = "   ";
    public const string Unavailable = "--";

    /// <summary>U+2007 FIGURE SPACE: as wide as a digit, so padded numbers keep the bar width stable.</summary>
    public const char FigureSpace = ' ';

    private const double BytesPerGb = 1024d * 1024 * 1024;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Format(IReadOnlyList<MetricKey> keys, MetricsSnapshot snapshot, bool showSeparator)
    {
        if (keys.Count == 0)
        {
            return Strings.NoMetricsSelected;
        }

        var sb = new StringBuilder();
        foreach (var key in keys)
        {
            if (sb.Length > 0)
            {
                sb.Append(showSeparator ? Separator : NoSeparator);
            }
            sb.Append(FormatItem(key, snapshot.Get(key)));
        }
        return sb.ToString();
    }

    public static string FormatItem(MetricKey key, MetricReading? reading)
    {
        var ok = reading is { HasValue: true };
        var value = reading?.Value ?? 0;
        return key.Kind switch
        {
            MetricKind.CpuUsage => "CPU " + (ok ? Percent(value) : Unavailable),
            MetricKind.GpuUsage => "GPU " + (ok ? Percent(value) : Unavailable),
            MetricKind.MemoryPercent => "RAM " + (ok ? Percent(value) : Unavailable),
            MetricKind.VramPercent => "VRAM " + (ok ? Percent(value) : Unavailable),
            MetricKind.MemoryUsedTotal => "RAM " + (ok && reading!.Total is { } t1 ? UsedTotal(value, t1) : Unavailable),
            MetricKind.VramUsedTotal => "VRAM " + (ok && reading!.Total is { } t2 ? UsedTotal(value, t2) : Unavailable),
            MetricKind.DiskUsedPercent => $"{key.Drive}: " + (ok ? Percent(value) : Unavailable),
            MetricKind.DiskFree => ok
                ? string.Format(Invariant, Strings.DiskFreeFormat, key.Drive, FreeGb(value))
                : $"{key.Drive}: {Unavailable}",
            _ => key.Id,
        };
    }

    /// <summary>
    /// Bar tooltip: the full text, each value's source (e.g. the GPU name), why unavailable
    /// values are missing, and the sampling time.
    /// </summary>
    public static string BuildTooltip(IReadOnlyList<MetricKey> keys, MetricsSnapshot snapshot, string barText)
    {
        var lines = new List<string> { barText };
        var seen = new HashSet<string>();
        foreach (var key in keys)
        {
            var reading = snapshot.Get(key);
            var label = key.IsDisk ? string.Format(Invariant, Strings.DriveLabelFormat, key.Drive) : ShortLabel(key);
            string? line = null;
            if (reading is { HasValue: true, Source: { } source })
            {
                line = string.Format(Invariant, Strings.TooltipSourceFormat, SourceLabel(key), source);
            }
            else if (reading is { HasValue: false, Reason: { } reason })
            {
                line = string.Format(Invariant, Strings.TooltipUnavailableFormat, label, reason);
            }
            if (line is not null && seen.Add(line))
            {
                lines.Add(line);
            }
        }

        if (snapshot.Timestamp != DateTimeOffset.MinValue)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.BarSampledAtFormat, snapshot.Timestamp));
        }
        if (snapshot.IsSimulated)
        {
            lines.Add(Strings.BarSimulatedNote);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The prefix the bar shows for a metric: CPU, GPU, RAM, VRAM or the drive (C:).</summary>
    public static string ShortLabel(MetricKey key) => key.Kind switch
    {
        MetricKind.CpuUsage => "CPU",
        MetricKind.GpuUsage => "GPU",
        MetricKind.MemoryUsedTotal or MetricKind.MemoryPercent => "RAM",
        MetricKind.VramUsedTotal or MetricKind.VramPercent => "VRAM",
        _ => $"{key.Drive}:",
    };

    // GPU and VRAM come from the same adapter; name it once as "GPU".
    private static string SourceLabel(MetricKey key) =>
        key.Kind is MetricKind.VramUsedTotal or MetricKind.VramPercent ? "GPU" : ShortLabel(key);

    /// <summary>Rounded 0-100 integer, left-padded to two digits with figure spaces.</summary>
    public static string Percent(double percent)
    {
        var rounded = (int)Math.Round(Math.Clamp(percent, 0, 100), MidpointRounding.AwayFromZero);
        return rounded.ToString(Invariant).PadLeft(2, FigureSpace) + "%";
    }

    /// <summary><c>9.2/15.9 GB</c>, with the used part padded to the width of the total part.</summary>
    public static string UsedTotal(double usedBytes, double totalBytes)
    {
        var total = (totalBytes / BytesPerGb).ToString("0.0", Invariant);
        var used = (Math.Clamp(usedBytes, 0, totalBytes) / BytesPerGb).ToString("0.0", Invariant);
        return used.PadLeft(total.Length, FigureSpace) + "/" + total + " GB";
    }

    private static string FreeGb(double freeBytes)
    {
        var gb = Math.Max(0, freeBytes) / BytesPerGb;
        return gb < 10 ? gb.ToString("0.0", Invariant) : gb.ToString("0", Invariant);
    }
}
