using Microsoft.Extensions.Time.Testing;
using SysMonitorBar.Core.Logging;

namespace SysMonitorBar.Tests;

public sealed class RollingFileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SysMonitorBar.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void WritesTimestampedLinesAndSkipsBelowMinimumLevel()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 9, 30, 15, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        using (var log = new RollingFileLog(_dir, time: time))
        {
            log.Write(LogLevel.Debug, "hidden");
            log.Write(LogLevel.Info, "started");
            log.Write(LogLevel.Error, "boom", new InvalidOperationException("bad"));
        }

        var text = File.ReadAllText(Path.Combine(_dir, "SysMonitorBar.log"));

        Assert.DoesNotContain("hidden", text);
        Assert.StartsWith("2026-10-22 09:30:15.000 INF started", text);
        Assert.Contains("ERR boom", text);
        Assert.Contains("System.InvalidOperationException: bad", text);
    }

    [Fact]
    public void RotatesAndKeepsAtMostMaxFiles()
    {
        using (var log = new RollingFileLog(_dir, maxBytes: 200, maxFiles: 3))
        {
            for (var i = 0; i < 50; i++)
            {
                log.Write(LogLevel.Info, $"line {i:D2} " + new string('x', 40));
            }
        }

        var files = Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToList();

        Assert.Equal(["SysMonitorBar.1.log", "SysMonitorBar.2.log", "SysMonitorBar.log"], files);
        Assert.Contains("line 49", File.ReadAllText(Path.Combine(_dir, "SysMonitorBar.log")) +
                                   File.ReadAllText(Path.Combine(_dir, "SysMonitorBar.1.log")));
        Assert.All(Directory.GetFiles(_dir), f => Assert.True(new FileInfo(f).Length < 400));
    }
}
