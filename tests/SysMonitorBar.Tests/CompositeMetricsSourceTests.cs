using Microsoft.Extensions.Time.Testing;
using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Sampling;

namespace SysMonitorBar.Tests;

public class CompositeMetricsSourceTests
{
    private static readonly MetricKey Cpu = MetricKey.Of(MetricKind.CpuUsage);
    private static readonly MetricKey Ram = MetricKey.Of(MetricKind.MemoryPercent);
    private static readonly MetricKey Gpu = MetricKey.Of(MetricKind.GpuUsage);

    private sealed class FakeProvider(string name, MetricKind kind, double value) : IMetricProvider
    {
        public int Calls;
        public bool Throw;

        public string Name => name;

        public bool Supports(MetricKey key) => key.Kind == kind;

        public void Sample(IReadOnlyCollection<MetricKey> keys, DateTimeOffset at, IDictionary<MetricKey, MetricReading> output)
        {
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException($"{name} broke");
            }
            foreach (var key in keys)
            {
                output[key] = MetricReading.Percent(key, value, at);
            }
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void OnlyProvidersWithRequestedKeysRun()
    {
        var cpu = new FakeProvider("cpu", MetricKind.CpuUsage, 10);
        var gpu = new FakeProvider("gpu", MetricKind.GpuUsage, 20);
        using var source = new CompositeMetricsSource([cpu, gpu]);

        var snapshot = source.Sample([Cpu]);

        Assert.Equal(10, snapshot.Get(Cpu)!.Value);
        Assert.Equal(1, cpu.Calls);
        Assert.Equal(0, gpu.Calls);
    }

    [Fact]
    public void FailingProviderDoesNotAffectOthersAndBacksOff()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cpu = new FakeProvider("cpu", MetricKind.CpuUsage, 10);
        var gpu = new FakeProvider("gpu", MetricKind.GpuUsage, 20) { Throw = true };
        var errors = new List<string>();
        using var source = new CompositeMetricsSource([cpu, gpu], time, (name, _) => errors.Add(name));

        var first = source.Sample([Cpu, Gpu]);

        Assert.True(first.Get(Cpu)!.HasValue);
        Assert.Equal(MetricQuality.Unavailable, first.Get(Gpu)!.Quality);
        Assert.Equal("gpu broke", first.Get(Gpu)!.Reason);
        Assert.Equal(["gpu"], errors);

        // Within the 5 s back-off the GPU provider is not called again.
        time.Advance(TimeSpan.FromSeconds(4));
        var second = source.Sample([Cpu, Gpu]);
        Assert.Equal(1, gpu.Calls);
        Assert.Equal(MetricQuality.Unavailable, second.Get(Gpu)!.Quality);

        // After it, the provider is retried and recovers.
        gpu.Throw = false;
        time.Advance(TimeSpan.FromSeconds(2));
        var third = source.Sample([Cpu, Gpu]);
        Assert.Equal(2, gpu.Calls);
        Assert.Equal(20, third.Get(Gpu)!.Value);
    }

    [Fact]
    public void KeysWithoutProviderAreUnavailable()
    {
        using var source = new CompositeMetricsSource([new FakeProvider("cpu", MetricKind.CpuUsage, 10)]);

        var snapshot = source.Sample([Cpu, Ram]);

        Assert.Equal(MetricQuality.Unavailable, snapshot.Get(Ram)!.Quality);
    }
}

public class FailureBackoffTests
{
    [Fact]
    public void DoublesFromFiveToSixtySecondsAndResetsOnSuccess()
    {
        var backoff = new FailureBackoff();
        var now = DateTimeOffset.UnixEpoch;

        Assert.Equal(TimeSpan.FromSeconds(5), backoff.RecordFailure(now));
        Assert.Equal(TimeSpan.FromSeconds(10), backoff.RecordFailure(now));
        Assert.Equal(TimeSpan.FromSeconds(20), backoff.RecordFailure(now));
        Assert.Equal(TimeSpan.FromSeconds(40), backoff.RecordFailure(now));
        Assert.Equal(TimeSpan.FromSeconds(60), backoff.RecordFailure(now));
        Assert.Equal(TimeSpan.FromSeconds(60), backoff.RecordFailure(now));
        Assert.True(backoff.ShouldSkip(now + TimeSpan.FromSeconds(59)));
        Assert.False(backoff.ShouldSkip(now + TimeSpan.FromSeconds(60)));

        backoff.RecordSuccess();
        Assert.False(backoff.ShouldSkip(now));
        Assert.Equal(TimeSpan.FromSeconds(5), backoff.RecordFailure(now));
    }
}
