using System.Globalization;
using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Gpu;
using SysMonitorBar.Core.Logging;
using SysMonitorBar.Core.Resources;
using SysMonitorBar.Core.Sampling;

namespace SysMonitorBar.Infrastructure.Metrics;

/// <summary>
/// GPU usage and dedicated VRAM from the Windows GPU counters (the source Task Manager uses), for
/// the adapter chosen in settings or picked automatically. The total VRAM comes from DXGI.
/// Shared GPU memory is never counted as VRAM.
/// </summary>
internal sealed class GpuProvider : IMetricProvider
{
    private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";
    private const string MemoryPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private static readonly TimeSpan AdapterRefresh = TimeSpan.FromSeconds(60);

    private IReadOnlyList<GpuAdapterInfo> _adapters = [];
    private DateTimeOffset _adaptersAt = DateTimeOffset.MinValue;
    private PdhQuery? _engineQuery;
    private IntPtr _engineCounter;
    private PdhQuery? _memoryQuery;
    private IntPtr _memoryCounter;
    private volatile string? _preferredId;
    private string? _loggedSelection;

    public string Name => "GPU";

    /// <summary>Stable id of the adapter chosen in settings; null for automatic. Settable from any thread.</summary>
    public string? PreferredId
    {
        get => _preferredId;
        set => _preferredId = value;
    }

    public bool Supports(MetricKey key) =>
        key.Kind is MetricKind.GpuUsage or MetricKind.VramUsedTotal or MetricKind.VramPercent;

    public void Sample(IReadOnlyCollection<MetricKey> keys, DateTimeOffset at, IDictionary<MetricKey, MetricReading> output)
    {
        // Re-enumerate now and then: a driver reset or an eGPU changes the adapter set and LUIDs.
        if (at - _adaptersAt > AdapterRefresh)
        {
            _adapters = DxgiAdapters.Enumerate();
            _adaptersAt = at;
        }

        var selection = GpuSelector.Select(_adapters, PreferredId);
        if (selection.Adapter is not { } adapter)
        {
            foreach (var key in keys)
            {
                output[key] = MetricReading.Unavailable(key, at, Strings.ReasonNoGpu);
            }
            return;
        }

        LogSelection(selection);
        var source = selection.PreferredMissing
            ? string.Format(CultureInfo.CurrentCulture, Strings.GpuPreferredMissingFormat, adapter.Name)
            : adapter.Name;

        if (keys.Any(k => k.Kind == MetricKind.GpuUsage))
        {
            var key = MetricKey.Of(MetricKind.GpuUsage);
            EnsureEngineQuery();
            _engineQuery!.Collect();
            var usage = GpuCounterMath.UsageByAdapter(_engineQuery.GetArray(_engineCounter));
            output[key] = usage.TryGetValue(adapter.LuidKey, out var percent)
                ? MetricReading.Percent(key, percent, at) with { Source = source }
                : MetricReading.Unavailable(key, at, Strings.ReasonNoGpuCounter);
        }

        var vramKeys = keys.Where(k => k.Kind is MetricKind.VramUsedTotal or MetricKind.VramPercent).ToList();
        if (vramKeys.Count > 0)
        {
            EnsureMemoryQuery();
            _memoryQuery!.Collect();
            var used = GpuCounterMath.DedicatedUsageByAdapter(_memoryQuery.GetArray(_memoryCounter));
            double total = adapter.DedicatedVideoMemoryBytes;
            foreach (var key in vramKeys)
            {
                if (!used.TryGetValue(adapter.LuidKey, out var bytes) || total <= 0)
                {
                    output[key] = MetricReading.Unavailable(key, at, Strings.ReasonNoGpuCounter);
                    continue;
                }
                var reading = key.Kind == MetricKind.VramPercent
                    ? MetricReading.Percent(key, bytes / total * 100, at)
                    : MetricReading.Bytes(key, bytes, total, at);
                output[key] = reading with { Source = source };
            }
        }
    }

    private void LogSelection(GpuSelection selection)
    {
        var description = selection.Adapter is { } a
            ? $"{a.Name} ({a.Id}, {a.DedicatedVideoMemoryBytes / (1024 * 1024)} MB dedicated)" +
              (selection.PreferredMissing ? $"; preferred {PreferredId} not found" : string.Empty)
            : "none";
        if (description != _loggedSelection)
        {
            _loggedSelection = description;
            Log.Info($"GPU: {description}.");
        }
    }

    /// <summary>Re-enumerates adapters and reopens the counter queries on the next sample.</summary>
    public void Reset()
    {
        Dispose();
        _adaptersAt = DateTimeOffset.MinValue;
    }

    public void Dispose()
    {
        _engineQuery?.Dispose();
        _engineQuery = null;
        _memoryQuery?.Dispose();
        _memoryQuery = null;
    }

    private void EnsureEngineQuery()
    {
        if (_engineQuery is not null)
        {
            return;
        }
        var query = new PdhQuery();
        try
        {
            _engineCounter = query.AddCounter(EnginePath);
            // Utilization is a rate; give it a first collection to compare against.
            query.Collect();
            Thread.Sleep(200);
        }
        catch
        {
            query.Dispose();
            throw;
        }
        _engineQuery = query;
    }

    private void EnsureMemoryQuery()
    {
        if (_memoryQuery is not null)
        {
            return;
        }
        var query = new PdhQuery();
        try
        {
            _memoryCounter = query.AddCounter(MemoryPath);
        }
        catch
        {
            query.Dispose();
            throw;
        }
        _memoryQuery = query;
    }
}
