using System.Globalization;

namespace SysMonitorBar.Core.Gpu;

/// <summary>
/// A display adapter as DXGI reports it.
/// <see cref="Id"/> is stable across reboots (vendor/device/subsystem/revision plus an ordinal for
/// identical cards) and is what settings store; <see cref="LuidKey"/> changes per boot and links
/// the adapter to its performance-counter instances.
/// </summary>
public sealed record GpuAdapterInfo(
    string Id,
    string Name,
    string LuidKey,
    ulong DedicatedVideoMemoryBytes,
    bool IsSoftware)
{
    /// <summary>The LUID as it appears in counter instance names, e.g. <c>0x00000000_0x0000d673</c>.</summary>
    public static string FormatLuid(int highPart, uint lowPart) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{(uint)highPart:x8}_0x{lowPart:x8}");

    public static string FormatId(uint vendorId, uint deviceId, uint subSysId, uint revision, int ordinal)
    {
        var id = string.Create(CultureInfo.InvariantCulture, $"VEN_{vendorId:X4}&DEV_{deviceId:X4}&SUBSYS_{subSysId:X8}&REV_{revision:X2}");
        return ordinal == 0 ? id : string.Create(CultureInfo.InvariantCulture, $"{id}#{ordinal}");
    }
}
