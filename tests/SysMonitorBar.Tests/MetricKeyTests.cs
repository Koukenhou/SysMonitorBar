using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Tests;

public class MetricKeyTests
{
    [Theory]
    [InlineData("cpu.usage")]
    [InlineData("gpu.usage")]
    [InlineData("memory.usedTotal")]
    [InlineData("memory.percent")]
    [InlineData("gpu.vramUsedTotal")]
    [InlineData("gpu.vramPercent")]
    [InlineData("disk.C.usedPercent")]
    [InlineData("disk.D.free")]
    public void TryParse_RoundTripsKnownIds(string id)
    {
        Assert.True(MetricKey.TryParse(id, out var key));
        Assert.Equal(id, key.Id);
    }

    [Fact]
    public void TryParse_NormalizesDriveLetterToUpperCase()
    {
        Assert.True(MetricKey.TryParse("disk.d.usedPercent", out var key));
        Assert.Equal('D', key.Drive);
        Assert.Equal("disk.D.usedPercent", key.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cpu.temperature")]
    [InlineData("disk.CD.usedPercent")]
    [InlineData("disk.1.usedPercent")]
    [InlineData("disk.C.read")]
    [InlineData("disk.C")]
    public void TryParse_RejectsUnknownIds(string? id)
    {
        Assert.False(MetricKey.TryParse(id, out _));
    }

    [Fact]
    public void Of_RejectsDiskKinds()
    {
        Assert.Throws<ArgumentException>(() => MetricKey.Of(MetricKind.DiskFree));
    }

    [Fact]
    public void Order_PutsNonDiskMetricsFirstThenDisksByDrive()
    {
        var keys = new[]
        {
            MetricKey.Disk(MetricKind.DiskFree, 'C'),
            MetricKey.Disk(MetricKind.DiskUsedPercent, 'D'),
            MetricKey.Of(MetricKind.VramUsedTotal),
            MetricKey.Disk(MetricKind.DiskUsedPercent, 'C'),
            MetricKey.Of(MetricKind.CpuUsage),
        };

        var ordered = keys.Order().Select(k => k.Id);

        Assert.Equal(
            ["cpu.usage", "gpu.vramUsedTotal", "disk.C.usedPercent", "disk.C.free", "disk.D.usedPercent"],
            ordered);
    }
}
