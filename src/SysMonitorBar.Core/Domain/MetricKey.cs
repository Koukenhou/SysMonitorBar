namespace SysMonitorBar.Core.Domain;

/// <summary>Kind of a displayable metric. Declaration order is the canonical display order.</summary>
public enum MetricKind
{
    CpuUsage,
    GpuUsage,
    MemoryUsedTotal,
    MemoryPercent,
    VramUsedTotal,
    VramPercent,
    DiskUsedPercent,
    DiskFree,
}

/// <summary>
/// A metric kind plus its instance (the drive letter for disk metrics).
/// Persisted as the string ids of design v1.0 §9, e.g. <c>cpu.usage</c> or <c>disk.C.usedPercent</c>.
/// </summary>
public readonly record struct MetricKey : IComparable<MetricKey>
{
    private MetricKey(MetricKind kind, char? drive)
    {
        Kind = kind;
        Drive = drive;
    }

    public MetricKind Kind { get; }

    /// <summary>Upper-case drive letter for disk metrics; null otherwise.</summary>
    public char? Drive { get; }

    public bool IsDisk => IsDiskKind(Kind);

    public static MetricKey Of(MetricKind kind)
    {
        if (IsDiskKind(kind))
        {
            throw new ArgumentException($"{kind} needs a drive letter; use {nameof(Disk)}.", nameof(kind));
        }
        return new MetricKey(kind, null);
    }

    public static MetricKey Disk(MetricKind kind, char drive)
    {
        if (!IsDiskKind(kind))
        {
            throw new ArgumentException($"{kind} is not a disk metric.", nameof(kind));
        }
        drive = char.ToUpperInvariant(drive);
        if (drive is < 'A' or > 'Z')
        {
            throw new ArgumentOutOfRangeException(nameof(drive), drive, "Drive letter must be A-Z.");
        }
        return new MetricKey(kind, drive);
    }

    public string Id => Kind switch
    {
        MetricKind.CpuUsage => "cpu.usage",
        MetricKind.GpuUsage => "gpu.usage",
        MetricKind.MemoryUsedTotal => "memory.usedTotal",
        MetricKind.MemoryPercent => "memory.percent",
        MetricKind.VramUsedTotal => "gpu.vramUsedTotal",
        MetricKind.VramPercent => "gpu.vramPercent",
        MetricKind.DiskUsedPercent => $"disk.{Drive}.usedPercent",
        MetricKind.DiskFree => $"disk.{Drive}.free",
        _ => throw new InvalidOperationException($"Unknown metric kind {Kind}."),
    };

    public static bool TryParse(string? id, out MetricKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        switch (id.Trim())
        {
            case "cpu.usage": key = Of(MetricKind.CpuUsage); return true;
            case "gpu.usage": key = Of(MetricKind.GpuUsage); return true;
            case "memory.usedTotal": key = Of(MetricKind.MemoryUsedTotal); return true;
            case "memory.percent": key = Of(MetricKind.MemoryPercent); return true;
            case "gpu.vramUsedTotal": key = Of(MetricKind.VramUsedTotal); return true;
            case "gpu.vramPercent": key = Of(MetricKind.VramPercent); return true;
        }

        var parts = id.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != "disk" || parts[1].Length != 1 || !char.IsAsciiLetter(parts[1][0]))
        {
            return false;
        }

        MetricKind? kind = parts[2] switch
        {
            "usedPercent" => MetricKind.DiskUsedPercent,
            "free" => MetricKind.DiskFree,
            _ => null,
        };
        if (kind is null)
        {
            return false;
        }

        key = Disk(kind.Value, parts[1][0]);
        return true;
    }

    /// <summary>Canonical order: non-disk metrics by kind, then disks by drive letter, then kind.</summary>
    public int CompareTo(MetricKey other)
    {
        if (IsDisk != other.IsDisk)
        {
            return IsDisk ? 1 : -1;
        }
        if (IsDisk)
        {
            var byDrive = Nullable.Compare(Drive, other.Drive);
            if (byDrive != 0)
            {
                return byDrive;
            }
        }
        return Kind.CompareTo(other.Kind);
    }

    public override string ToString() => Id;

    private static bool IsDiskKind(MetricKind kind) => kind is MetricKind.DiskUsedPercent or MetricKind.DiskFree;
}
