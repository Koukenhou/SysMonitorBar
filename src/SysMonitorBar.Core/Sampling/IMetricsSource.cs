using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Core.Sampling;

/// <summary>
/// Produces one snapshot per call. Called only from the coordinator's single background loop,
/// so implementations never see concurrent calls. A failing metric is reported as unavailable,
/// not thrown.
/// </summary>
public interface IMetricsSource : IDisposable
{
    MetricsSnapshot Sample(IReadOnlyCollection<MetricKey> keys);

    /// <summary>
    /// Drops cached native state (counter queries, adapter lists) so the next sample starts fresh,
    /// e.g. after the machine wakes from sleep. Called on the sampling loop's thread.
    /// </summary>
    void Reset();
}
