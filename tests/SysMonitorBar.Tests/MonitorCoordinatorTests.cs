using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Sampling;

namespace SysMonitorBar.Tests;

public class MonitorCoordinatorTests
{
    private static readonly IReadOnlyList<MetricKey> Keys = [MetricKey.Of(MetricKind.CpuUsage)];

    private sealed class CountingSource(TimeSpan sampleDuration) : IMetricsSource
    {
        private int _active;

        public int Calls;
        public int MaxConcurrent;
        public bool Throw;

        public MetricsSnapshot Sample(IReadOnlyCollection<MetricKey> keys)
        {
            var active = Interlocked.Increment(ref _active);
            MaxConcurrent = Math.Max(MaxConcurrent, active);
            Thread.Sleep(sampleDuration);
            Interlocked.Increment(ref Calls);
            Interlocked.Decrement(ref _active);
            if (Throw)
            {
                throw new InvalidOperationException("sensor failed");
            }
            return MetricsSnapshot.Empty;
        }

        public int Resets;

        public void Reset() => Interlocked.Increment(ref Resets);

        public void Dispose()
        {
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for condition.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task SlowSamplesNeverOverlap()
    {
        var source = new CountingSource(TimeSpan.FromMilliseconds(30));
        await using var coordinator = new MonitorCoordinator(source, _ => { });
        coordinator.Configure(Keys, TimeSpan.FromMilliseconds(1));
        coordinator.Start();

        // Configure/Resume calls ask for extra samples while one is running.
        for (var i = 0; i < 20; i++)
        {
            coordinator.Resume();
            await Task.Delay(5);
        }
        await WaitUntil(() => source.Calls >= 5);

        Assert.Equal(1, source.MaxConcurrent);
    }

    [Fact]
    public async Task PauseStopsSamplingAndResumeSamplesImmediately()
    {
        var source = new CountingSource(TimeSpan.Zero);
        await using var coordinator = new MonitorCoordinator(source, _ => { });
        coordinator.Configure(Keys, TimeSpan.FromHours(1));
        coordinator.Start();
        await WaitUntil(() => source.Calls == 1);

        coordinator.Pause();
        coordinator.Configure(Keys, TimeSpan.FromHours(1)); // wakes the loop, which must see the pause
        await Task.Delay(100);
        var whilePaused = source.Calls;
        await Task.Delay(100);
        Assert.Equal(whilePaused, source.Calls);

        coordinator.Resume();
        await WaitUntil(() => source.Calls == whilePaused + 1);
    }

    [Fact]
    public async Task FailingSourceKeepsTheLoopAlive()
    {
        var source = new CountingSource(TimeSpan.Zero) { Throw = true };
        var errors = 0;
        await using var coordinator = new MonitorCoordinator(source, _ => { }, _ => Interlocked.Increment(ref errors));
        coordinator.Configure(Keys, TimeSpan.FromMilliseconds(10));
        coordinator.Start();

        await WaitUntil(() => Volatile.Read(ref errors) >= 3);
    }

    [Fact]
    public async Task RequestResetResetsBeforeTheNextSample()
    {
        var source = new CountingSource(TimeSpan.Zero);
        await using var coordinator = new MonitorCoordinator(source, _ => { });
        coordinator.Configure(Keys, TimeSpan.FromHours(1));
        coordinator.Start();
        // Configure before Start leaves one wake-up pending, so let the loop settle first.
        await WaitUntil(() => source.Calls >= 1);
        await Task.Delay(100);
        var settled = source.Calls;
        Assert.Equal(0, source.Resets);

        coordinator.RequestReset();

        await WaitUntil(() => source.Calls == settled + 1);
        Assert.Equal(1, source.Resets);
    }

    [Fact]
    public async Task StopAsyncEndsTheLoop()
    {
        var source = new CountingSource(TimeSpan.Zero);
        var coordinator = new MonitorCoordinator(source, _ => { });
        coordinator.Configure(Keys, TimeSpan.FromMilliseconds(10));
        coordinator.Start();
        await WaitUntil(() => source.Calls >= 2);

        await coordinator.StopAsync();
        var afterStop = source.Calls;
        await Task.Delay(100);

        Assert.Equal(afterStop, source.Calls);
        await coordinator.DisposeAsync();
    }
}
