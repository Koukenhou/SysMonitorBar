namespace SysMonitorBar.Core.Gpu;

public sealed record GpuSelection(GpuAdapterInfo? Adapter, bool PreferredMissing);

public static class GpuSelector
{
    /// <summary>
    /// The adapter to monitor: the preferred one when it is present; otherwise the hardware adapter
    /// with the most dedicated memory (normally the discrete GPU). Software adapters
    /// (Microsoft Basic Render Driver) are never chosen.
    /// </summary>
    public static GpuSelection Select(IReadOnlyList<GpuAdapterInfo> adapters, string? preferredId)
    {
        var hardware = adapters.Where(a => !a.IsSoftware).ToList();
        if (!string.IsNullOrEmpty(preferredId))
        {
            var preferred = hardware.FirstOrDefault(a => a.Id == preferredId);
            if (preferred is not null)
            {
                return new GpuSelection(preferred, PreferredMissing: false);
            }
        }

        var best = hardware.MaxBy(a => a.DedicatedVideoMemoryBytes);
        return new GpuSelection(best, PreferredMissing: !string.IsNullOrEmpty(preferredId));
    }
}
