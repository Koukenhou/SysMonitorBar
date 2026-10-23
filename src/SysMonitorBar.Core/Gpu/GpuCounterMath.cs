using System.Text.RegularExpressions;

namespace SysMonitorBar.Core.Gpu;

/// <summary>
/// Turns raw Windows GPU counter samples into per-adapter values, the same way Task Manager does:
/// an engine's utilization is the sum over all processes using it, and the adapter's usage is its
/// busiest engine.
/// </summary>
public static partial class GpuCounterMath
{
    // e.g. pid_11860_luid_0x00000000_0x0000d673_phys_0_eng_0_engtype_3D
    [GeneratedRegex(@"luid_(0x[0-9a-f]{8}_0x[0-9a-f]{8})_phys_(\d+)_eng_(\d+)_engtype_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EngineInstance();

    // e.g. luid_0x00000000_0x0000d673_phys_0
    [GeneratedRegex(@"^luid_(0x[0-9a-f]{8}_0x[0-9a-f]{8})_phys_\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdapterInstance();

    /// <summary>
    /// <c>\GPU Engine(*)\Utilization Percentage</c> samples → usage percent (0-100) per LUID key.
    /// Unparsable instance names are ignored.
    /// </summary>
    public static IReadOnlyDictionary<string, double> UsageByAdapter(IEnumerable<(string Instance, double Value)> engineSamples)
    {
        var perEngine = new Dictionary<(string Luid, string Phys, string Eng), double>();
        foreach (var (instance, value) in engineSamples)
        {
            var m = EngineInstance().Match(instance);
            if (!m.Success || !double.IsFinite(value))
            {
                continue;
            }
            var key = (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, m.Groups[3].Value);
            perEngine[key] = perEngine.GetValueOrDefault(key) + Math.Max(0, value);
        }

        var perAdapter = new Dictionary<string, double>();
        foreach (var ((luid, _, _), sum) in perEngine)
        {
            perAdapter[luid] = Math.Max(perAdapter.GetValueOrDefault(luid), Math.Min(100, sum));
        }
        return perAdapter;
    }

    /// <summary><c>\GPU Adapter Memory(*)\Dedicated Usage</c> samples → bytes per LUID key (summed over physical parts).</summary>
    public static IReadOnlyDictionary<string, double> DedicatedUsageByAdapter(IEnumerable<(string Instance, double Value)> memorySamples)
    {
        var result = new Dictionary<string, double>();
        foreach (var (instance, value) in memorySamples)
        {
            var m = AdapterInstance().Match(instance);
            if (!m.Success || !double.IsFinite(value))
            {
                continue;
            }
            var luid = m.Groups[1].Value.ToLowerInvariant();
            result[luid] = result.GetValueOrDefault(luid) + Math.Max(0, value);
        }
        return result;
    }
}
