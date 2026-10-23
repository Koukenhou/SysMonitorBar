namespace SysMonitorBar.Core.Sampling;

/// <summary>
/// Exponential back-off after consecutive failures of one source: the first retry waits
/// <see cref="MinDelay"/>, each further failure doubles the wait up to <see cref="MaxDelay"/>.
/// A success resets it.
/// </summary>
public sealed class FailureBackoff
{
    public static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;

    public int ConsecutiveFailures { get; private set; }

    public bool ShouldSkip(DateTimeOffset now) => ConsecutiveFailures > 0 && now < _retryAt;

    /// <summary>Records a failure and returns how long the source will now be skipped.</summary>
    public TimeSpan RecordFailure(DateTimeOffset now)
    {
        ConsecutiveFailures++;
        var factor = Math.Pow(2, Math.Min(ConsecutiveFailures - 1, 10));
        var delay = TimeSpan.FromTicks((long)Math.Min(MinDelay.Ticks * factor, MaxDelay.Ticks));
        _retryAt = now + delay;
        return delay;
    }

    public void RecordSuccess()
    {
        ConsecutiveFailures = 0;
        _retryAt = DateTimeOffset.MinValue;
    }
}
