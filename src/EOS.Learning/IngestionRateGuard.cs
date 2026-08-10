namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.1/§24.1's Knowledge Poisoning defense —
/// <c>IngestionRateGuard.exceeds_threshold(producer_role, window)</c>. The window is wall-clock
/// bucket anchored (never process-start-relative), so persisted state remains correct across
/// application restarts (WP-026 Implementation Approval Report, Hidden Issue 2). Store failures
/// propagate rather than silently becoming "not exceeded" (fail-closed on the poisoning defense
/// itself, matching this codebase's established "no silent swallow" precedent, e.g.
/// RetryManager/RollbackManager).
/// </summary>
public sealed class IngestionRateGuard(
    IIngestionRateGuardStore store, int windowSeconds, int thresholdCount)
{
    public async Task<bool> ExceedsThresholdAsync(string producerRole, CancellationToken cancellationToken = default)
    {
        var (windowStart, windowEnd) = ComputeWallClockBucket(DateTimeOffset.UtcNow, windowSeconds);
        var count = await store.IncrementAndGetCountAsync(producerRole, windowStart, windowEnd, cancellationToken);
        return count > thresholdCount;
    }

    private static (DateTimeOffset Start, DateTimeOffset End) ComputeWallClockBucket(DateTimeOffset now, int bucketSeconds)
    {
        var nowUnixSeconds = now.ToUnixTimeSeconds();
        var bucketStartUnixSeconds = nowUnixSeconds - (nowUnixSeconds % bucketSeconds);
        var start = DateTimeOffset.FromUnixTimeSeconds(bucketStartUnixSeconds);
        return (start, start.AddSeconds(bucketSeconds));
    }
}
