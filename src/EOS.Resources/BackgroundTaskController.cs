using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §10.6/§15.1: the live, measurement-driven gate
/// deciding whether a pending background job may run right now — never altering what the job
/// does (FR-RM10). Implements §15.1's algorithm exactly, in order:
/// <list type="number">
/// <item>CPU load vs. Warning threshold (defers unconditionally under contention, FR-RM4).</item>
/// <item><see cref="QuotaManager"/>'s class-quota-exhaustion check (FR-RM8).</item>
/// <item>Maintenance window / Idle-Time mode check (WP-022 Implementation Plan Decision D10:
/// Constitution Part 7 §7.2's Maintenance Windows have no implementation anywhere in this
/// codebase yet — a pre-existing, disclosed condition this WP neither introduces nor is
/// required to close; <c>job.mode == IdleTime</c> always passes this check, matching the one
/// case §15.1 itself carves out).</item>
/// </list>
/// </summary>
public sealed class BackgroundTaskController(
    Func<CapacityTier> getCurrentCpuTier,
    QuotaManager quotaManager,
    IBackgroundJobGrantedEventPublisher grantedEventPublisher,
    IBackgroundJobDeferredEventPublisher deferredEventPublisher)
{
    public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass, bool isIdleTimeMode = false)
    {
        // §19.4 (WP-022 Recovery Plan Slice R1/Finding F1): Starvation Prevention is checked
        // first, ahead of every §15.1 step, so it overrides "regardless of contention" — not
        // only quota exhaustion, but also sustained CPU load or maintenance-window denial.
        if (quotaManager.IsStarvationOverrideActive(resourceClass))
        {
            quotaManager.RecordGrant(resourceClass);
            grantedEventPublisher.PublishBackgroundJobGranted(jobId, resourceClass);
            return;
        }

        var cpuTier = getCurrentCpuTier();
        if (cpuTier != CapacityTier.Safe)
        {
            Defer(jobId, resourceClass, "CPU load is at or above the Warning threshold.");
            return;
        }

        if (quotaManager.IsClassQuotaExhausted(resourceClass))
        {
            // WP-022 Recovery Plan Slice R2/Finding F2: ResourceQuotaExhausted is published only
            // here, where the Quota Manager itself determined exhaustion — never from the shared
            // Defer helper, which also handles CPU- and maintenance-window-caused deferrals.
            quotaManager.PublishQuotaExhausted(resourceClass, ResourceType.BackgroundTasks);
            Defer(jobId, resourceClass, "Resource-class quota is exhausted.");
            return;
        }

        if (!isIdleTimeMode && !WithinMaintenanceWindow())
        {
            Defer(jobId, resourceClass, "Outside the configured maintenance window.");
            return;
        }

        quotaManager.RecordGrant(resourceClass);
        grantedEventPublisher.PublishBackgroundJobGranted(jobId, resourceClass);
    }

    private void Defer(string jobId, ResourceClass resourceClass, string reason)
    {
        quotaManager.RecordDenial(resourceClass);
        deferredEventPublisher.PublishBackgroundJobDeferred(jobId, reason);
    }

    // WP-022 Implementation Plan Decision D10: no Maintenance-Window data source exists anywhere
    // in this codebase to legally consult (Constitution Part 7 §7.2 names the concept but no
    // project implements or exposes it yet) — disclosed identically to QuotaManager's Sprint-
    // cycle substitution (Decision D4), not a fabricated business rule.
    private static bool WithinMaintenanceWindow() => true;
}
