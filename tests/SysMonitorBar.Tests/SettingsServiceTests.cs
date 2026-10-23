using Microsoft.Extensions.Time.Testing;
using SysMonitorBar.Core.Settings;

namespace SysMonitorBar.Tests;

public sealed class SettingsServiceTests : IDisposable
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
    public void Load_MissingFileReturnsDefaults()
    {
        var result = new SettingsService(_dir).Load();

        Assert.Equal(SettingsLoadStatus.Missing, result.Status);
        Assert.Equal(DisplaySettings.DefaultMetricIds, result.Settings.Display.MetricIds);
        Assert.Equal(0.8, result.Settings.Display.BackgroundOpacity);
        Assert.Equal(1000, result.Settings.Sampling.IntervalMs);
        Assert.False(result.Settings.Startup.StartWithWindows);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var service = new SettingsService(_dir);
        var settings = AppSettings.CreateDefault();
        settings.Display.BackgroundOpacity = 0.45;
        settings.Display.FontSize = 16;
        settings.Display.MetricIds = ["memory.percent", "disk.D.free"];
        settings.Display.Position = new PositionSettings { ScreenDeviceName = @"\\.\DISPLAY2", LeftPx = 1700, TopPx = 20 };
        settings.Sampling.IntervalMs = 2000;

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal(0.45, loaded.Settings.Display.BackgroundOpacity);
        Assert.Equal(16, loaded.Settings.Display.FontSize);
        Assert.Equal(["memory.percent", "disk.D.free"], loaded.Settings.Display.MetricIds);
        Assert.Equal(@"\\.\DISPLAY2", loaded.Settings.Display.Position.ScreenDeviceName);
        Assert.Equal(1700, loaded.Settings.Display.Position.LeftPx);
        Assert.Equal(2000, loaded.Settings.Sampling.IntervalMs);
        Assert.False(File.Exists(service.FilePath + ".tmp"));
    }

    [Fact]
    public void Save_WritesCamelCaseJson()
    {
        var service = new SettingsService(_dir);
        service.Save(AppSettings.CreateDefault());

        var json = File.ReadAllText(service.FilePath);

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"backgroundOpacity\": 0.8", json);
        Assert.Contains("\"cpu.usage\"", json);
    }

    [Fact]
    public void Load_CorruptFileIsBackedUpAndDefaultsAreUsed()
    {
        Directory.CreateDirectory(_dir);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 22, 9, 30, 15, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var service = new SettingsService(_dir, time);
        File.WriteAllText(service.FilePath, "{ this is not json");

        var result = service.Load();

        Assert.Equal(SettingsLoadStatus.Corrupt, result.Status);
        Assert.Equal(Path.Combine(_dir, "settings.corrupt-20261022-093015.json"), result.CorruptBackupPath);
        Assert.True(File.Exists(result.CorruptBackupPath));
        Assert.False(File.Exists(service.FilePath));
        Assert.Equal(DisplaySettings.DefaultMetricIds, result.Settings.Display.MetricIds);
    }

    [Fact]
    public void Load_IgnoresUnknownFieldsAndFillsMissingSections()
    {
        Directory.CreateDirectory(_dir);
        var service = new SettingsService(_dir);
        File.WriteAllText(service.FilePath, """
            {
              "schemaVersion": 1,
              "futureField": { "x": 1 },
              "display": { "fontSize": 15, "metricIds": ["cpu.usage"] }
            }
            """);

        var result = service.Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(15, result.Settings.Display.FontSize);
        Assert.Equal(["cpu.usage"], result.Settings.Display.MetricIds);
        Assert.Equal(1000, result.Settings.Sampling.IntervalMs);
        Assert.NotNull(result.Settings.Display.Position);
    }

    [Fact]
    public void StageAPositionIsMigratedToPixelsAndNotWrittenBack()
    {
        Directory.CreateDirectory(_dir);
        var service = new SettingsService(_dir);
        File.WriteAllText(service.FilePath, """
            { "display": { "position": { "screenDeviceName": "D1", "leftDip": 7, "topDip": 823.4 } } }
            """);

        var settings = service.Load().Settings;
        settings.Display.Position.MigrateFromDip(1.25);
        service.Save(settings);

        Assert.Equal(9, settings.Display.Position.LeftPx);
        Assert.Equal(1029, settings.Display.Position.TopPx);
        Assert.DoesNotContain("leftDip", File.ReadAllText(service.FilePath));
    }

    [Fact]
    public void Normalize_ClampsValuesAndDropsUnknownMetricIds()
    {
        var s = AppSettings.CreateDefault();
        s.Display.BackgroundOpacity = 0.05;
        s.Display.FontSize = 99;
        s.Display.MaxWidthRatio = double.NaN;
        s.Display.MetricIds = ["cpu.usage", "cpu.temperature", "cpu.usage", "disk.C.usedPercent"];
        s.Display.Position.LeftPx = 100; // without TopPx
        s.Sampling.IntervalMs = 1700;

        SettingsService.Normalize(s);

        Assert.Equal(DisplaySettings.MinBackgroundOpacity, s.Display.BackgroundOpacity);
        Assert.Equal(DisplaySettings.MaxFontSize, s.Display.FontSize);
        Assert.Equal(0.9, s.Display.MaxWidthRatio);
        Assert.Equal(["cpu.usage", "disk.C.usedPercent"], s.Display.MetricIds);
        Assert.Null(s.Display.Position.LeftPx);
        Assert.Equal(2000, s.Sampling.IntervalMs);
    }
}
