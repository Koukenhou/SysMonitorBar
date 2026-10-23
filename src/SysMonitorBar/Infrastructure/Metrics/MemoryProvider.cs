using System.ComponentModel;
using System.Runtime.InteropServices;
using SysMonitorBar.Core.Domain;
using SysMonitorBar.Core.Sampling;

namespace SysMonitorBar.Infrastructure.Metrics;

/// <summary>Physical memory from <c>GlobalMemoryStatusEx</c>: used = total - available.</summary>
internal sealed class MemoryProvider : IMetricProvider
{
    public string Name => "Memory";

    public bool Supports(MetricKey key) => key.Kind is MetricKind.MemoryUsedTotal or MetricKind.MemoryPercent;

    public void Sample(IReadOnlyCollection<MetricKey> keys, DateTimeOffset at, IDictionary<MetricKey, MetricReading> output)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        double total = status.TotalPhys;
        var used = total - status.AvailPhys;
        foreach (var key in keys)
        {
            output[key] = key.Kind == MetricKind.MemoryPercent
                ? MetricReading.Percent(key, total > 0 ? used / total * 100 : 0, at)
                : MetricReading.Bytes(key, used, total, at);
        }
    }

    public void Dispose()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
