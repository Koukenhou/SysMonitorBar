using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Core.Sampling;

/// <summary>
/// Combines providers into one snapshot. Only providers with a requested key run. A provider that
/// throws gets its keys marked unavailable and is skipped for a back-off period, so one broken
/// sensor never takes down the others. Keys no provider answered are reported unavailable.
/// </summary>
public sealed class CompositeMetricsSource : IMetricsSource
{
    private readonly IReadOnlyList<IMetricProvider> _providers;
    private readonly Dictionary<IMetricProvider, FailureBackoff> _backoff = [];
    private readonly Dictionary<IMetricProvider, string> _lastError = [];
    private readonly TimeProvider _time;
    private readonly Action<string, Exception>? _onProviderError;
    private readonly Action<string>? _onProviderRecovered;

    public CompositeMetricsSource(IEnumerable<IMetricProvider> providers, TimeProvider? time = null,
        Action<string, Exception>? onProviderError = null, Action<string>? onProviderRecovered = null)
    {
        _providers = [.. providers];
        _time = time ?? TimeProvider.System;
        _onProviderError = onProviderError;
        _onProviderRecovered = onProviderRecovered;
        foreach (var p in _providers)
        {
            _backoff[p] = new FailureBackoff();
        }
    }

    public MetricsSnapshot Sample(IReadOnlyCollection<MetricKey> keys)
    {
        var now = _time.GetLocalNow();
        var values = new Dictionary<MetricKey, MetricReading>();

        foreach (var provider in _providers)
        {
            var mine = keys.Where(provider.Supports).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var backoff = _backoff[provider];
            if (backoff.ShouldSkip(now))
            {
                MarkUnavailable(mine, now, values, _lastError.GetValueOrDefault(provider));
                continue;
            }

            var output = new Dictionary<MetricKey, MetricReading>();
            try
            {
                provider.Sample(mine, now, output);
                if (backoff.ConsecutiveFailures > 0)
                {
                    _onProviderRecovered?.Invoke(provider.Name);
                }
                backoff.RecordSuccess();
                _lastError.Remove(provider);
                foreach (var key in mine)
                {
                    if (output.TryGetValue(key, out var reading))
                    {
                        values[key] = reading;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                backoff.RecordFailure(now);
                _lastError[provider] = ex.Message;
                _onProviderError?.Invoke(provider.Name, ex);
                MarkUnavailable(mine, now, values, ex.Message);
            }
        }

        foreach (var key in keys)
        {
            if (!values.ContainsKey(key))
            {
                values[key] = MetricReading.Unavailable(key, now);
            }
        }
        return new MetricsSnapshot(now, values);
    }

    public void Reset()
    {
        foreach (var p in _providers)
        {
            p.Reset();
            _backoff[p].RecordSuccess();
        }
        _lastError.Clear();
    }

    public void Dispose()
    {
        foreach (var p in _providers)
        {
            p.Dispose();
        }
    }

    private static void MarkUnavailable(IEnumerable<MetricKey> keys, DateTimeOffset now,
        Dictionary<MetricKey, MetricReading> values, string? reason)
    {
        foreach (var key in keys)
        {
            values[key] = MetricReading.Unavailable(key, now, reason);
        }
    }
}
