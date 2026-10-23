using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Formatting;

namespace SysMonitorBar.Tests;

public class MetricFormatterTests
{
    private const double Gb = 1024d * 1024 * 1024;
    private const char Fs = MetricFormatter.FigureSpace;
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(9));

    private static readonly MetricKey Cpu = MetricKey.Of(MetricKind.CpuUsage);
    private static readonly MetricKey Gpu = MetricKey.Of(MetricKind.GpuUsage);
    private static readonly MetricKey Ram = MetricKey.Of(MetricKind.MemoryUsedTotal);
    private static readonly MetricKey Vram = MetricKey.Of(MetricKind.VramUsedTotal);
    private static readonly MetricKey DiskC = MetricKey.Disk(MetricKind.DiskUsedPercent, 'C');

    private static MetricsSnapshot Snapshot(params MetricReading[] readings) =>
        new(At, readings.ToDictionary(r => r.Key));

    [Fact]
    public void Format_DesignExampleLine()
    {
        var snapshot = Snapshot(
            MetricReading.Percent(Cpu, 18, At),
            MetricReading.Percent(Gpu, 6, At),
            MetricReading.Bytes(Ram, 9.2 * Gb, 16 * Gb, At),
            MetricReading.Bytes(Vram, 2.1 * Gb, 16 * Gb, At),
            MetricReading.Percent(DiskC, 62, At));

        var text = MetricFormatter.Format([Cpu, Gpu, Ram, Vram, DiskC], snapshot, showSeparator: true);

        Assert.Equal($"CPU 18% | GPU {Fs}6% | RAM {Fs}9.2/16.0 GB | VRAM {Fs}2.1/16.0 GB | C: 62%", text);
    }

    [Fact]
    public void Format_MissingOrUnavailableReadingsShowDashesNotZero()
    {
        var snapshot = Snapshot(
            MetricReading.Percent(Cpu, 18, At),
            MetricReading.Unavailable(Gpu, At, "no GPU"));

        var text = MetricFormatter.Format([Cpu, Gpu, Vram], snapshot, showSeparator: true);

        Assert.Equal("CPU 18% | GPU -- | VRAM --", text);
    }

    [Fact]
    public void Format_WithoutSeparatorUsesSpaces()
    {
        var snapshot = Snapshot(MetricReading.Percent(Cpu, 18, At), MetricReading.Percent(DiskC, 62, At));

        Assert.Equal("CPU 18%   C: 62%", MetricFormatter.Format([Cpu, DiskC], snapshot, showSeparator: false));
    }

    [Fact]
    public void Format_NoKeysShowsPlaceholderMessage()
    {
        Assert.Equal("表示項目が選択されていません", MetricFormatter.Format([], MetricsSnapshot.Empty, showSeparator: true));
    }

    [Theory]
    [InlineData(0, " 0%")]
    [InlineData(6.4, " 6%")]
    [InlineData(9.5, "10%")]
    [InlineData(99.6, "100%")]
    [InlineData(130, "100%")]
    [InlineData(-3, " 0%")]
    public void Percent_RoundsClampsAndPads(double value, string expected)
    {
        Assert.Equal(expected, MetricFormatter.Percent(value));
    }

    [Fact]
    public void UsedTotal_PadsUsedToTotalWidth()
    {
        Assert.Equal($"{Fs}{Fs}9.2/128.0 GB", MetricFormatter.UsedTotal(9.2 * Gb, 128 * Gb));
        Assert.Equal("15.9/15.9 GB", MetricFormatter.UsedTotal(20 * Gb, 15.9 * Gb));
    }

    [Fact]
    public void DiskFree_UsesOneDecimalBelowTenGb()
    {
        var free = MetricKey.Disk(MetricKind.DiskFree, 'D');

        Assert.Equal("D: 空き 83 GB", MetricFormatter.FormatItem(free, MetricReading.Bytes(free, 83.4 * Gb, null, At)));
        Assert.Equal("D: 空き 7.5 GB", MetricFormatter.FormatItem(free, MetricReading.Bytes(free, 7.5 * Gb, null, At)));
        Assert.Equal("D: --", MetricFormatter.FormatItem(free, null));
    }

    [Fact]
    public void BuildTooltip_NamesGpuOnceAndExplainsMissingValues()
    {
        var snapshot = Snapshot(
            MetricReading.Percent(Gpu, 6, At) with { Source = "NVIDIA GeForce GTX 1650" },
            MetricReading.Bytes(Vram, 1.6 * Gb, 4 * Gb, At) with { Source = "NVIDIA GeForce GTX 1650" },
            MetricReading.Unavailable(DiskC, At, "ドライブを読み取れません"));

        var tip = MetricFormatter.BuildTooltip([Gpu, Vram, DiskC], snapshot, "BAR");

        Assert.Equal(
            ["BAR", "GPU: NVIDIA GeForce GTX 1650", "C ドライブ: 取得できません（ドライブを読み取れません）", "取得時刻: 12:00:00"],
            tip.Split(Environment.NewLine));
    }

    [Fact]
    public void UsedTotalWithoutTotalIsUnavailable()
    {
        Assert.Equal("RAM --", MetricFormatter.FormatItem(Ram, MetricReading.Bytes(Ram, 9 * Gb, null, At)));
    }
}
