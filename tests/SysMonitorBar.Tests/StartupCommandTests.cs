using SysMonitorBar.Core.Startup;

namespace SysMonitorBar.Tests;

public class StartupCommandTests
{
    private const string Exe = @"C:\Tools\SysMonitorBar\SysMonitorBar.exe";

    [Fact]
    public void For_QuotesThePath()
    {
        Assert.Equal("\"C:\\Program Files\\SysMonitorBar\\SysMonitorBar.exe\"",
            StartupCommand.For(@"C:\Program Files\SysMonitorBar\SysMonitorBar.exe"));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\SysMonitorBar.exe\" --x", @"C:\Program Files\App\SysMonitorBar.exe")]
    [InlineData(@"C:\Tools\SysMonitorBar.exe --minimized", @"C:\Tools\SysMonitorBar.exe")]
    [InlineData(@"  C:\Tools\SysMonitorBar.exe  ", @"C:\Tools\SysMonitorBar.exe")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("\"", null)]
    public void ExePathOf_HandlesQuotesAndArguments(string? command, string? expected)
    {
        Assert.Equal(expected, StartupCommand.ExePathOf(command));
    }

    [Fact]
    public void NeedsRepair_OnlyWhenTheRegisteredExeIsGone()
    {
        var old = "\"D:\\Old\\SysMonitorBar.exe\"";

        Assert.True(StartupCommand.NeedsRepair(old, Exe, _ => false));
        Assert.False(StartupCommand.NeedsRepair(old, Exe, _ => true));           // another copy still exists
        Assert.False(StartupCommand.NeedsRepair(StartupCommand.For(Exe.ToUpperInvariant()), Exe, _ => false));
        Assert.False(StartupCommand.NeedsRepair(null, Exe, _ => false));
    }

    [Fact]
    public void IsDisabledByUser_ReadsTheLowBitOfTheFirstByte()
    {
        Assert.True(StartupCommand.IsDisabledByUser([0x03, 0, 0]));
        Assert.False(StartupCommand.IsDisabledByUser([0x02, 0, 0]));
        Assert.False(StartupCommand.IsDisabledByUser([]));
        Assert.False(StartupCommand.IsDisabledByUser(null));
    }
}
