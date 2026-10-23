using System.IO;
using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Resources;
using SysMonitorBar.Core.Sampling;

namespace SysMonitorBar.Infrastructure.Metrics;

/// <summary>
/// Volume capacity from <see cref="DriveInfo"/>. This is space on the drive letter, not physical
/// disk activity. Only fixed drives are read; a missing or unreadable drive is reported
/// unavailable without affecting other drives.
/// </summary>
internal sealed class DiskProvider : IMetricProvider
{
    public string Name => "Disk";

    public bool Supports(MetricKey key) => key.IsDisk;

    public void Sample(IReadOnlyCollection<MetricKey> keys, DateTimeOffset at, IDictionary<MetricKey, MetricReading> output)
    {
        foreach (var group in keys.GroupBy(k => k.Drive!.Value))
        {
            try
            {
                var drive = new DriveInfo(group.Key + @":\");
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize <= 0)
                {
                    MarkUnavailable(group, at, output);
                    continue;
                }

                double total = drive.TotalSize;
                foreach (var key in group)
                {
                    output[key] = key.Kind == MetricKind.DiskUsedPercent
                        ? MetricReading.Percent(key, (total - drive.TotalFreeSpace) / total * 100, at)
                        : MetricReading.Bytes(key, drive.AvailableFreeSpace, total, at);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MarkUnavailable(group, at, output);
            }
        }
    }

    public void Dispose()
    {
    }

    private static void MarkUnavailable(IEnumerable<MetricKey> keys, DateTimeOffset at, IDictionary<MetricKey, MetricReading> output)
    {
        foreach (var key in keys)
        {
            output[key] = MetricReading.Unavailable(key, at, Strings.ReasonDriveNotReady);
        }
    }
}
