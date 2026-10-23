using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SysMonitorBar.Infrastructure.Metrics;

/// <summary>
/// Thin wrapper over the Performance Data Helper API. Counters are added by their English path
/// (<c>PdhAddEnglishCounter</c>), so it works on any Windows display language; .NET's
/// <c>PerformanceCounter</c> needs localized names and does not even list the GPU categories.
/// Not thread-safe: use from the sampling loop only.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_CSTATUS_VALID_DATA = 0x0;
    private const uint PDH_CSTATUS_NEW_DATA = 0x1;

    private IntPtr _query;

    public PdhQuery()
    {
        Check(PdhOpenQueryW(null, IntPtr.Zero, out _query), "PdhOpenQuery");
    }

    /// <summary>Adds a counter by its English path, e.g. <c>\Processor Information(_Total)\% Processor Utility</c>.</summary>
    public IntPtr AddCounter(string englishPath)
    {
        Check(PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var counter), englishPath);
        return counter;
    }

    public void Collect() => Check(PdhCollectQueryData(_query), "PdhCollectQueryData");

    /// <summary>The counter's value, or null when it has no valid data yet (rate counters need two collections).</summary>
    public double? TryGetDouble(IntPtr counter)
    {
        var status = PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value);
        return status == 0 && value.CStatus is PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA ? value.DoubleValue : null;
    }

    /// <summary>All instances of a wildcard counter that have valid data.</summary>
    public List<(string Instance, double Value)> GetArray(IntPtr counter)
    {
        var result = new List<(string, double)>();
        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA)
        {
            // No instances (or no valid data yet) is not an error for a wildcard counter.
            return result;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Check(PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, buffer),
                "PdhGetFormattedCounterArray");
            // PDH_FMT_COUNTERVALUE_ITEM_W: { LPWSTR szName; PDH_FMT_COUNTERVALUE FmtValue { DWORD CStatus; double } }
            var itemSize = IntPtr.Size + 16;
            for (var i = 0; i < count; i++)
            {
                var item = buffer + i * itemSize;
                var cstatus = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                if (cstatus is not (PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA))
                {
                    continue;
                }
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? string.Empty;
                var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8));
                result.Add((name, value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    private static void Check(uint status, string what)
    {
        if (status != 0)
        {
            throw new Win32Exception(unchecked((int)status), $"{what} failed (0x{status:X8}).");
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
