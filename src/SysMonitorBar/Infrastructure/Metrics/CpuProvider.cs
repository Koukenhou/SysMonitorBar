using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Resources;
using SysMonitorBar.Core.Sampling;

namespace SysMonitorBar.Infrastructure.Metrics;

/// <summary>
/// CPU usage as Task Manager shows it: <c>% Processor Utility</c> (frequency-aware, can exceed
/// 100; the formatter caps it). Falls back to the time-based <c>% Processor Time</c> where the
/// utility counter does not exist.
/// </summary>
internal sealed class CpuProvider : IMetricProvider
{
    private const string UtilityPath = @"\Processor Information(_Total)\% Processor Utility";
    private const string TimePath = @"\Processor(_Total)\% Processor Time";

    private PdhQuery? _query;
    private IntPtr _counter;

    public string Name => "CPU";

    public bool Supports(MetricKey key) => key.Kind == MetricKind.CpuUsage;

    public void Sample(IReadOnlyCollection<MetricKey> keys, DateTimeOffset at, IDictionary<MetricKey, MetricReading> output)
    {
        EnsureOpen();
        _query!.Collect();
        var key = MetricKey.Of(MetricKind.CpuUsage);
        output[key] = _query.TryGetDouble(_counter) is { } value
            ? MetricReading.Percent(key, value, at)
            : MetricReading.Unavailable(key, at, Strings.ReasonWarmingUp);
    }

    public void Reset() => Dispose();

    public void Dispose()
    {
        _query?.Dispose();
        _query = null;
    }

    private void EnsureOpen()
    {
        if (_query is not null)
        {
            return;
        }

        var query = new PdhQuery();
        try
        {
            try
            {
                _counter = query.AddCounter(UtilityPath);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _counter = query.AddCounter(TimePath);
            }
            // Rate counters need a first collection to compare against.
            query.Collect();
            Thread.Sleep(200);
        }
        catch
        {
            query.Dispose();
            throw;
        }
        _query = query;
    }
}
