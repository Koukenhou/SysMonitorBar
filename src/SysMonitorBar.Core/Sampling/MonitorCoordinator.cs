using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Core.Sampling;

/// <summary>
/// Runs the single background sampling loop. Samples are never taken concurrently and never
/// queue up: a slow sample simply delays the next one. Snapshots are handed to
/// <c>onSnapshot</c> on the loop's thread; the UI side marshals them to its dispatcher.
/// </summary>
public sealed class MonitorCoordinator : IAsyncDisposable
{
    private readonly IMetricsSource _source;
    private readonly Action<MetricsSnapshot> _onSnapshot;
    private readonly Action<Exception>? _onError;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();

    private IReadOnlyList<MetricKey> _keys = [];
    private TimeSpan _interval = TimeSpan.FromSeconds(1);
    private bool _paused;
    private volatile bool _resetRequested;
    private Task? _loop;

    public MonitorCoordinator(IMetricsSource source, Action<MetricsSnapshot> onSnapshot, Action<Exception>? onError = null)
    {
        _source = source;
        _onSnapshot = onSnapshot;
        _onError = onError;
    }

    /// <summary>Number of completed sampling passes (for diagnostics and tests).</summary>
    public int SampleCount { get; private set; }

    public bool IsPaused
    {
        get { lock (_gate) { return _paused; } }
    }

    public void Configure(IReadOnlyList<MetricKey> keys, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        lock (_gate)
        {
            _keys = [.. keys];
            _interval = interval;
        }
        Wake();
    }

    public void Start()
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException("Already started.");
        }
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
        }
    }

    /// <summary>Resumes sampling and takes a fresh sample immediately.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            _paused = false;
        }
        Wake();
    }

    /// <summary>Asks the loop to reset the source before its next sample (e.g. after resume from sleep).</summary>
    public void RequestReset()
    {
        _resetRequested = true;
        Wake();
    }

    public async Task StopAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }
        _stop.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _source.Dispose();
        _stop.Dispose();
        _wake.Dispose();
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<MetricKey> keys;
            TimeSpan interval;
            bool paused;
            lock (_gate)
            {
                keys = _keys;
                interval = _interval;
                paused = _paused;
            }

            if (paused)
            {
                await _wake.WaitAsync(ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                if (_resetRequested)
                {
                    _resetRequested = false;
                    _source.Reset();
                }
                var snapshot = _source.Sample(keys);
                SampleCount++;
                _onSnapshot(snapshot);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _onError?.Invoke(ex);
            }

            // Returns early when Configure/Resume asks for a fresh sample.
            await _wake.WaitAsync(interval, ct).ConfigureAwait(false);
        }
    }
}
