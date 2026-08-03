using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §10.4: fair-share resource-class Quotas (§19.1-§19.2)
/// and Starvation Prevention (§19.4). Distinct from Protection's per-action ceiling check (checks
/// one action against the total budget, not fairness across concurrent classes).
///
/// §15.1's <c>class_quota_exhausted(resource_class)</c> has no frozen "job completed" signal to
/// track true in-flight concurrency (§22's State Model has no <c>Granted → Completed</c>
/// transition, §20 defines no such event) — resolved via the same elapsed-time-window counting
/// mechanism <see cref="ResourceMonitor"/> already uses for its sampling throttle (WP-022
/// Implementation Plan Decision D9): grants within the current window count against the class's
/// Model-slot quota; the window resets once <paramref name="windowSeconds"/> elapses.
/// </summary>
public sealed class QuotaManager(
    ResourceClassQuotas quotas,
    int starvationDenialCountThreshold,
    int windowSeconds,
    IResourceQuotaExhaustedEventPublisher eventPublisher)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<ResourceClass, int> _consecutiveDenials = [];
    private readonly Dictionary<ResourceClass, (DateTimeOffset WindowStart, int GrantsInWindow)> _grantWindows = [];

    /// <summary>
    /// §19.4: "guaranteed a minimum allocation slice on the next cycle regardless of
    /// contention" — checked by <see cref="BackgroundTaskController"/> before any other §15.1
    /// step (WP-022 Recovery Plan Slice R1/Finding F1), so the override applies to every
    /// contention cause, not only quota exhaustion.
    /// </summary>
    public bool IsStarvationOverrideActive(ResourceClass resourceClass)
    {
        lock (_lock)
        {
            return _consecutiveDenials.TryGetValue(resourceClass, out var denials) && denials >= starvationDenialCountThreshold;
        }
    }

    /// <summary>
    /// §15.1: "if QuotaManager.class_quota_exhausted(resource_class): defer(job)" — checked
    /// against the class's Model-slot quota (§14.4's "identical in mechanism to CPU/RAM fair-
    /// share quotas"), the one dimension a generic background-job request can be measured against
    /// without the caller declaring a specific resource type (§21.1's signature carries no
    /// resource-type parameter).
    /// </summary>
    public bool IsClassQuotaExhausted(ResourceClass resourceClass)
    {
        lock (_lock)
        {
            // §19.4: a class starved past the configured threshold is guaranteed its next slot
            // regardless of contention, overriding the quota check for exactly one grant.
            if (_consecutiveDenials.TryGetValue(resourceClass, out var denials) && denials >= starvationDenialCountThreshold)
            {
                return false;
            }

            var quota = GetQuota(resourceClass);
            var now = DateTimeOffset.UtcNow;
            if (_grantWindows.TryGetValue(resourceClass, out var window) && (now - window.WindowStart).TotalSeconds < windowSeconds)
            {
                return window.GrantsInWindow >= quota.ModelSlotCount;
            }

            return quota.ModelSlotCount <= 0;
        }
    }

    public void RecordGrant(ResourceClass resourceClass)
    {
        lock (_lock)
        {
            _consecutiveDenials[resourceClass] = 0;

            var now = DateTimeOffset.UtcNow;
            if (_grantWindows.TryGetValue(resourceClass, out var window) && (now - window.WindowStart).TotalSeconds < windowSeconds)
            {
                _grantWindows[resourceClass] = (window.WindowStart, window.GrantsInWindow + 1);
            }
            else
            {
                _grantWindows[resourceClass] = (now, 1);
            }
        }
    }

    /// <summary>
    /// §19.4's starvation counter — incremented for every deferral, regardless of cause, so
    /// Starvation Prevention (<see cref="IsStarvationOverrideActive"/>) can activate "regardless
    /// of contention." Never publishes an event itself — see <see cref="PublishQuotaExhausted"/>
    /// (WP-022 Recovery Plan Slice R2/Finding F2: these are two distinct responsibilities).
    /// </summary>
    public void RecordDenial(ResourceClass resourceClass)
    {
        lock (_lock)
        {
            _consecutiveDenials[resourceClass] = _consecutiveDenials.GetValueOrDefault(resourceClass) + 1;
        }
    }

    /// <summary>
    /// §20's <c>ResourceQuotaExhausted</c> — published only by the caller that actually
    /// determined a class's quota is exhausted (<see cref="IsClassQuotaExhausted"/> returned
    /// <see langword="true"/>), never for any other deferral cause (WP-022 Recovery Plan Slice
    /// R2/Finding F2).
    /// </summary>
    public void PublishQuotaExhausted(ResourceClass resourceClass, ResourceType resourceType) =>
        eventPublisher.PublishResourceQuotaExhausted(resourceClass, resourceType);

    private ResourceClassQuota GetQuota(ResourceClass resourceClass) => resourceClass switch
    {
        ResourceClass.UserRequests => quotas.UserRequests,
        ResourceClass.InteractiveSessions => quotas.InteractiveSessions,
        ResourceClass.AutonomousTasks => quotas.AutonomousTasks,
        ResourceClass.BackgroundMaintenance => quotas.BackgroundMaintenance,
        ResourceClass.LearningActivities => quotas.LearningActivities,
        _ => throw new ArgumentOutOfRangeException(nameof(resourceClass), resourceClass, "Unsupported ResourceClass."),
    };
}
