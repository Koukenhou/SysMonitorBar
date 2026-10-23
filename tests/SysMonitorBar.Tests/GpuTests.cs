using SysMonitorBar.Core.Gpu;

namespace SysMonitorBar.Tests;

public class GpuCounterMathTests
{
    private const string Nvidia = "0x00000000_0x0000d673";
    private const string Other = "0x00000000_0x0000e3df";

    [Fact]
    public void UsageByAdapter_SumsProcessesPerEngineAndTakesBusiestEngine()
    {
        var samples = new (string, double)[]
        {
            ($"pid_100_luid_{Nvidia}_phys_0_eng_0_engtype_3D", 20),
            ($"pid_200_luid_{Nvidia}_phys_0_eng_0_engtype_3D", 15),       // engine 0: 35
            ($"pid_100_luid_{Nvidia}_phys_0_eng_3_engtype_VideoDecode", 30), // engine 3: 30
            ($"pid_300_luid_{Other}_phys_0_eng_0_engtype_3D", 4),
        };

        var usage = GpuCounterMath.UsageByAdapter(samples);

        Assert.Equal(35, usage[Nvidia]);
        Assert.Equal(4, usage[Other]);
    }

    [Fact]
    public void UsageByAdapter_CapsAt100AndIgnoresBadInput()
    {
        var samples = new (string, double)[]
        {
            ($"pid_1_luid_{Nvidia}_phys_0_eng_0_engtype_3D", 80),
            ($"pid_2_luid_{Nvidia}_phys_0_eng_0_engtype_3D", 70),
            ("not_a_gpu_instance", 99),
            ($"pid_3_luid_{Other}_phys_0_eng_0_engtype_3D", double.NaN),
        };

        var usage = GpuCounterMath.UsageByAdapter(samples);

        Assert.Equal(100, usage[Nvidia]);
        Assert.False(usage.ContainsKey(Other));
    }

    [Fact]
    public void UsageByAdapter_IsCaseInsensitiveOnLuid()
    {
        var usage = GpuCounterMath.UsageByAdapter([("pid_1_luid_0x00000000_0x0000D673_phys_0_eng_0_engtype_3D", 12)]);

        Assert.Equal(12, usage[Nvidia]);
    }

    [Fact]
    public void DedicatedUsageByAdapter_ReadsAdapterInstances()
    {
        var used = GpuCounterMath.DedicatedUsageByAdapter(
        [
            ($"luid_{Nvidia}_phys_0", 1697812480),
            ($"luid_{Other}_phys_0", 0),
            ($"pid_1_luid_{Nvidia}_phys_0", 999), // process-level instance from another category
        ]);

        Assert.Equal(1697812480, used[Nvidia]);
        Assert.Equal(0, used[Other]);
        Assert.Equal(2, used.Count);
    }

    [Fact]
    public void FormatLuid_MatchesCounterInstanceNames()
    {
        Assert.Equal(Nvidia, GpuAdapterInfo.FormatLuid(0, 0xd673));
        Assert.Equal("0xffffffff_0x00000001", GpuAdapterInfo.FormatLuid(-1, 1));
    }

    [Fact]
    public void FormatId_AddsOrdinalForIdenticalCards()
    {
        Assert.Equal("VEN_10DE&DEV_1F82&SUBSYS_12345678&REV_A1", GpuAdapterInfo.FormatId(0x10DE, 0x1F82, 0x12345678, 0xA1, 0));
        Assert.Equal("VEN_10DE&DEV_1F82&SUBSYS_12345678&REV_A1#1", GpuAdapterInfo.FormatId(0x10DE, 0x1F82, 0x12345678, 0xA1, 1));
    }
}

public class GpuSelectorTests
{
    private const ulong Gb = 1024UL * 1024 * 1024;
    private static readonly GpuAdapterInfo Integrated = new("intel", "Intel UHD", "0x0_0x1", Gb / 8, false);
    private static readonly GpuAdapterInfo Discrete = new("nvidia", "GeForce GTX 1650", "0x0_0x2", 4 * Gb, false);
    private static readonly GpuAdapterInfo Software = new("basic", "Microsoft Basic Render Driver", "0x0_0x3", 0, true);

    [Fact]
    public void Automatic_PrefersMostDedicatedMemory()
    {
        var selection = GpuSelector.Select([Integrated, Discrete, Software], null);

        Assert.Same(Discrete, selection.Adapter);
        Assert.False(selection.PreferredMissing);
    }

    [Fact]
    public void PreferredAdapterWins()
    {
        Assert.Same(Integrated, GpuSelector.Select([Integrated, Discrete], "intel").Adapter);
    }

    [Fact]
    public void MissingPreferredFallsBackAndSaysSo()
    {
        var selection = GpuSelector.Select([Integrated], "nvidia");

        Assert.Same(Integrated, selection.Adapter);
        Assert.True(selection.PreferredMissing);
    }

    [Fact]
    public void SoftwareOnlyMeansNoGpu()
    {
        Assert.Null(GpuSelector.Select([Software], null).Adapter);
        Assert.Null(GpuSelector.Select([], null).Adapter);
    }
}
