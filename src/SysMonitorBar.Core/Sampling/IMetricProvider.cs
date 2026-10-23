using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Core.Sampling;

/// <summary>
/// One data source (CPU counters, memory API, drives, GPU counters, ...). Called only from the
/// sampling loop. A provider writes a reading for each requested key it supports; it may report
/// individual keys as unavailable, or throw when the whole source failed (the composite then
/// marks all of its keys unavailable and backs off).
/// </summary>
public interface IMetricProvider : IDisposable
{
    /// <summary>Short name for diagnostics.</summary>
    string Name { get; }

    bool Supports(MetricKey key);

    void Sample(IReadOnlyCollection<MetricKey> keys, DateTimeOffset at, IDictionary<MetricKey, MetricReading> output);

    /// <summary>Drops cached native state; see <see cref="IMetricsSource.Reset"/>.</summary>
    void Reset()
    {
    }
}
