using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Core.Sampling;

/// <summary>Plausible random-walk values for stage A (UI only, no real sensors).</summary>
public sealed class SimulatedMetricsSource : IMetricsSource
{
    private const double Gb = 1024d * 1024 * 1024;
    private const double MemoryTotal = 15.9 * Gb;
    private const double VramTotal = 4.0 * Gb;
    private const double DiskTotal = 476 * Gb;

    private readonly Random _random;
    private readonly TimeProvider _time;
    private readonly Dictionary<MetricKey, double> _levels = [];

    public SimulatedMetricsSource(int? seed = null, TimeProvider? time = null)
    {
        _random = seed is { } s ? new Random(s) : new Random();
        _time = time ?? TimeProvider.System;
    }

    public MetricsSnapshot Sample(IReadOnlyCollection<MetricKey> keys)
    {
        var now = _time.GetLocalNow();
        var values = new Dictionary<MetricKey, MetricReading>();
        foreach (var key in keys)
        {
            values[key] = key.Kind switch
            {
                MetricKind.CpuUsage => MetricReading.Percent(key, Walk(key, 18, 12), now),
                MetricKind.GpuUsage => MetricReading.Percent(key, Walk(key, 6, 8), now),
                MetricKind.MemoryPercent => MetricReading.Percent(key, Walk(key, 58, 2), now),
                MetricKind.MemoryUsedTotal => MetricReading.Bytes(key, Walk(key, 58, 2) / 100 * MemoryTotal, MemoryTotal, now),
                MetricKind.VramPercent => MetricReading.Percent(key, Walk(key, 40, 3), now),
                MetricKind.VramUsedTotal => MetricReading.Bytes(key, Walk(key, 40, 3) / 100 * VramTotal, VramTotal, now),
                MetricKind.DiskUsedPercent => MetricReading.Percent(key, Walk(key, 62, 0.1), now),
                MetricKind.DiskFree => MetricReading.Bytes(key, (100 - Walk(key, 62, 0.1)) / 100 * DiskTotal, null, now),
                _ => MetricReading.Unavailable(key, now),
            };
        }
        return new MetricsSnapshot(now, values, IsSimulated: true);
    }

    public void Reset()
    {
    }

    public void Dispose()
    {
    }

    private double Walk(MetricKey key, double start, double step)
    {
        // Mean-reverting walk around the start value.
        var level = _levels.GetValueOrDefault(key, start);
        level += (start - level) * 0.2 + (_random.NextDouble() * 2 - 1) * step;
        level = Math.Clamp(level, 0, 100);
        _levels[key] = level;
        return level;
    }
}
