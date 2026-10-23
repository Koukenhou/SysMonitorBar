using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SysMonitorBar.Core.Domain;

namespace SysMonitorBar.Core.Settings;

internal static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

public enum SettingsLoadStatus
{
    Loaded,
    Missing,
    Corrupt,
}

public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status, string? CorruptBackupPath = null);

/// <summary>Reads and writes <c>settings.json</c>. Writes go to a temp file first and then replace the target.</summary>
public sealed class SettingsService
{
    public const string FileName = "settings.json";

    private readonly TimeProvider _time;

    public SettingsService(string directory, TimeProvider? time = null)
    {
        Directory = directory;
        _time = time ?? TimeProvider.System;
    }

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysMonitorBar");

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, FileName);

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsLoadResult(AppSettings.CreateDefault(), SettingsLoadStatus.Missing);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), SettingsJson.Options)
                ?? throw new JsonException("settings.json contains null.");
            return new SettingsLoadResult(Normalize(settings), SettingsLoadStatus.Loaded);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var backup = Path.Combine(Directory, $"settings.corrupt-{stamp}.json");
            File.Move(FilePath, backup, overwrite: true);
            return new SettingsLoadResult(AppSettings.CreateDefault(), SettingsLoadStatus.Corrupt, backup);
        }
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(Normalize(settings.Clone()), SettingsJson.Options);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);

        // Verify what was written before it replaces the current file.
        _ = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(temp), SettingsJson.Options)
            ?? throw new JsonException("Failed to verify the written settings.");
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Fills missing sections and clamps values into their valid ranges. Mutates and returns <paramref name="s"/>.</summary>
    public static AppSettings Normalize(AppSettings s)
    {
        s.Display ??= new DisplaySettings();
        s.Sampling ??= new SamplingSettings();
        s.Startup ??= new StartupSettings();
        s.Display.Position ??= new PositionSettings();
        s.SchemaVersion = AppSettings.CurrentSchemaVersion;

        var d = s.Display;
        d.BackgroundOpacity = ClampOr(d.BackgroundOpacity, DisplaySettings.MinBackgroundOpacity, DisplaySettings.MaxBackgroundOpacity, 0.8);
        d.FontSize = Math.Round(ClampOr(d.FontSize, DisplaySettings.MinFontSize, DisplaySettings.MaxFontSize, 13));
        d.MaxWidthRatio = ClampOr(d.MaxWidthRatio, DisplaySettings.MinMaxWidthRatio, DisplaySettings.MaxMaxWidthRatio, 0.9);
        d.MetricIds = (d.MetricIds ?? [.. DisplaySettings.DefaultMetricIds])
            .Select(id => MetricKey.TryParse(id, out var key) ? key.Id : null)
            .OfType<string>()
            .Distinct()
            .ToList();

        var p = d.Position;
        if (p.LeftPx is null != p.TopPx is null)
        {
            p.LeftPx = null;
            p.TopPx = null;
        }

        var allowed = SamplingSettings.AllowedIntervalsMs;
        if (!allowed.Contains(s.Sampling.IntervalMs))
        {
            s.Sampling.IntervalMs = allowed.MinBy(ms => Math.Abs(ms - s.Sampling.IntervalMs));
        }
        return s;
    }

    private static double ClampOr(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
