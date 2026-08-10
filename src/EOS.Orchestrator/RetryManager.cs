using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.9/§13.7-13.8: implements Constitution Part 6
/// §6.2's <c>Blocked → Retry → Running</c> transition — same <see cref="DispatchedTask.TaskId"/>
/// throughout, never a new Task. The intermediate <c>Retry</c> state is not persisted (not
/// required by any frozen document); the operation performs the transition atomically from the
/// caller's perspective, exactly as <see cref="ExecutionCoordinator.DispatchNextAsync"/> already
/// does for <c>Ready → Running</c>.
///
/// §13.8: "timeout uses the same Retry Rule evaluation trigger, not a separate mechanism" — a
/// <see cref="TaskLifecycleState.Running"/> Task whose <see cref="DispatchedTask.RunningAt"/> has
/// exceeded <c>retryTimeoutSeconds</c> is evaluated through this exact same method, never a
/// second timeout mechanism.
///
/// Retry budget: <see cref="DispatchedTask.RetryCount"/> &gt;= <c>retryMaxAttemptsCount</c> leaves
/// the Task permanently <see cref="TaskLifecycleState.Blocked"/> (§13.7), never a new terminal
/// state. Backoff: no dedicated "became Blocked at" timestamp exists on <see cref="DispatchedTask"/>
/// (WP-025.1 added only <c>RetryCount</c>/<c>BlockedReason</c>) — <see cref="DispatchedTask.RunningAt"/>
/// (the last time this Task was actually Running, the only existing timestamp available) is
/// reused as the backoff anchor rather than inventing a new field, a disclosed, minimal
/// interpretation matching this codebase's established simplification precedent (e.g.
/// <c>SchedulerConcurrencyCeilingCount</c>'s "one global ceiling").
/// </summary>
public sealed class RetryManager(
    DispatchedTaskStore store,
    IProtectionClient protectionClient,
    ITaskRetriedEventPublisher taskRetriedEventPublisher,
    int retryMaxAttemptsCount,
    int retryBackoffDelaySeconds,
    int retryTimeoutSeconds)
{
    /// <summary>
    /// Evaluates <paramref name="task"/> for retry and, if eligible, performs the
    /// <c>Blocked → Retry → Running</c> transition. <paramref name="task"/> must be either
    /// already <see cref="TaskLifecycleState.Blocked"/>, or <see cref="TaskLifecycleState.Running"/>
    /// and timed out (§13.8) — any other state is a caller error.
    /// </summary>
    public async Task<DispatchedTask> RetryAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var timedOut = HasTimedOut(task, now);

        if (task.State != TaskLifecycleState.Blocked && !timedOut)
        {
            throw new InvalidOperationException(
                $"Task '{task.TaskId}' is not eligible for retry evaluation (state: {task.State}).");
        }

        // §13.7: exhaustion leaves the Task permanently Blocked — never a new terminal state,
        // never retried further. A timed-out Running Task that is also exhausted must still have
        // its Running → Blocked transition actually committed (Constitution §6.2); an
        // already-Blocked, exhausted Task's re-write is a harmless no-op upsert of unchanged
        // values.
        if (task.RetryCount >= retryMaxAttemptsCount)
        {
            var exhausted = task with { State = TaskLifecycleState.Blocked };
            await store.UpsertAsync(exhausted, cancellationToken);
            return exhausted;
        }

        // §13.7's backoff_strategy has no formula defined by any frozen document — not yet
        // eligible is represented as a no-op (matching Scheduler.SelectNextDispatchableTaskAsync's
        // own "nothing eligible" precedent of returning without a write), never a fabricated
        // written state.
        if (!IsBackoffElapsed(task, now))
        {
            return task;
        }

        // Constitution Part 6 §6.2: "Retry → Running" uses "Same gates as Ready → Running" — the
        // exact same IProtectionClient.Validate call ExecutionCoordinator.DispatchNextAsync
        // already makes, same ActionType/Actor/RiskScore, never a second Protection abstraction.
        var validation = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: "TaskDispatch",
            Actor: "Scheduler",
            RiskScore: 10));

        if (validation.Verdict != ProtectionVerdict.Allow)
        {
            // Never overriding a Deny (Protection-Layer-Specification-v1.0 §25.1) — the Task is
            // left untouched, re-evaluated on a future retry evaluation pass.
            return task;
        }

        var retried = task with
        {
            State = TaskLifecycleState.Running,
            RunningAt = now,
            RetryCount = task.RetryCount + 1,
            BlockedReason = null,
        };
        await store.UpsertAsync(retried, cancellationToken);

        // Persist-then-publish: TaskRetried is a factual statement that the retry transition
        // already succeeded (WP-025 Architecture Board Ruling Q3's discipline, applied here to
        // Retry) — published only after the write above has already committed.
        taskRetriedEventPublisher.PublishTaskRetried(retried.TaskId, retried.RetryCount);

        return retried;
    }

    private bool HasTimedOut(DispatchedTask task, DateTimeOffset now) =>
        task.State == TaskLifecycleState.Running
        && task.RunningAt is { } runningAt
        && now - runningAt > TimeSpan.FromSeconds(retryTimeoutSeconds);

    private bool IsBackoffElapsed(DispatchedTask task, DateTimeOffset now) =>
        task.RunningAt is not { } runningAt || now - runningAt >= TimeSpan.FromSeconds(retryBackoffDelaySeconds);
}
