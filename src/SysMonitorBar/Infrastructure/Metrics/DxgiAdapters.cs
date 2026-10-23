using System.Runtime.InteropServices;
using SysMonitorBar.Core.Gpu;

namespace SysMonitorBar.Infrastructure.Metrics;

/// <summary>Lists display adapters through DXGI (name, LUID, dedicated video memory).</summary>
internal static unsafe class DxgiAdapters
{
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    // IDXGIFactory1 vtable: IUnknown (0-2), IDXGIObject (3-6), IDXGIFactory (7-11), EnumAdapters1 = 12.
    private const int FactoryEnumAdapters1 = 12;
    // IDXGIAdapter1 vtable: IUnknown (0-2), IDXGIObject (3-6), IDXGIAdapter (7-9), GetDesc1 = 10.
    private const int AdapterGetDesc1 = 10;
    private const int UnknownRelease = 2;

    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static IReadOnlyList<GpuAdapterInfo> Enumerate()
    {
        var hr = CreateDXGIFactory1(IidFactory1, out var factory);
        Marshal.ThrowExceptionForHR(hr);

        var result = new List<GpuAdapterInfo>();
        var seen = new Dictionary<string, int>();
        try
        {
            var enumAdapters = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)VTable(factory)[FactoryEnumAdapters1];
            for (uint i = 0; ; i++)
            {
                IntPtr adapter;
                hr = enumAdapters(factory, i, &adapter);
                if (hr == DXGI_ERROR_NOT_FOUND)
                {
                    break;
                }
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, AdapterDesc1*, int>)VTable(adapter)[AdapterGetDesc1];
                    AdapterDesc1 desc;
                    Marshal.ThrowExceptionForHR(getDesc(adapter, &desc));

                    var name = new string(desc.Description).TrimEnd('\0').Trim();
                    var baseId = GpuAdapterInfo.FormatId(desc.VendorId, desc.DeviceId, desc.SubSysId, desc.Revision, 0);
                    var ordinal = seen.GetValueOrDefault(baseId);
                    seen[baseId] = ordinal + 1;
                    result.Add(new GpuAdapterInfo(
                        GpuAdapterInfo.FormatId(desc.VendorId, desc.DeviceId, desc.SubSysId, desc.Revision, ordinal),
                        name,
                        GpuAdapterInfo.FormatLuid(desc.LuidHighPart, desc.LuidLowPart),
                        desc.DedicatedVideoMemory,
                        (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
        return result;
    }

    /// <summary>Like <see cref="Enumerate"/>, but returns an empty list instead of throwing.</summary>
    public static IReadOnlyList<GpuAdapterInfo> TryEnumerate()
    {
        try
        {
            return Enumerate();
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
    }

    private static IntPtr* VTable(IntPtr comObject) => *(IntPtr**)comObject;

    private static void Release(IntPtr comObject)
    {
        if (comObject != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(comObject)[UnknownRelease])(comObject);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);
}
