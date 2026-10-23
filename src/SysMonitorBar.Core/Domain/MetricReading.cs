namespace SysMonitorBar.Core.Domain;

public enum MetricQuality
{
    Available,
    Unavailable,
    Stale,
}

/// <summary>
/// One sampled value, already normalized by the provider:
/// percent kinds carry 0-100 in <see cref="Value"/>; byte kinds carry bytes in
/// <see cref="Value"/> (used or free) and, for used/total kinds, <see cref="Total"/>.
/// </summary>
public sealed record MetricReading(
    MetricKey Key,
    double? Value,
    double? Total,
    DateTimeOffset SampledAt,
    MetricQuality Quality,
    string? Reason = null)
{
    /// <summary>Where the value came from (e.g. the GPU name), shown in the bar's tooltip.</summary>
    public string? Source { get; init; }

    public bool HasValue => Quality == MetricQuality.Available && Value is not null;

    public static MetricReading Percent(MetricKey key, double percent, DateTimeOffset at) =>
        new(key, percent, null, at, MetricQuality.Available);

    public static MetricReading Bytes(MetricKey key, double bytes, double? totalBytes, DateTimeOffset at) =>
        new(key, bytes, totalBytes, at, MetricQuality.Available);

    public static MetricReading Unavailable(MetricKey key, DateTimeOffset at, string? reason = null) =>
        new(key, null, null, at, MetricQuality.Unavailable, reason);
}

/// <summary>Immutable result of one sampling pass.</summary>
public sealed record MetricsSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<MetricKey, MetricReading> Values,
    bool IsSimulated = false)
{
    public static MetricsSnapshot Empty { get; } =
        new(DateTimeOffset.MinValue, new Dictionary<MetricKey, MetricReading>());

    public MetricReading? Get(MetricKey key) => Values.GetValueOrDefault(key);
}
