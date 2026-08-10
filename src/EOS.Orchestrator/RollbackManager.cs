using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.10/§19.2/FR-PE9: executes only the Rollback
/// Path already defined per-transition in Constitution Part 6 §6.2 — never invents one. Also the
/// designated recipient of Protection's <c>RollbackRequested</c> (Protection-Layer-Specification-
/// v1.0 §20/§21). WP-025 Architecture Board Ruling Q3 (frozen): a failed rollback propagates the
/// original exception unmodified — no retry, no escalation state, no <c>RollbackFailed</c>, no
/// compensation.
/// </summary>
public sealed class RollbackManager(
    DispatchedTaskStore store,
    IRollbackExecutedEventPublisher rollbackExecutedEventPublisher)
{
    /// <summary>
    /// Most recently observed <c>RollbackRequested</c> fields. KNOWN, DISCLOSED GAP (mirrors
    /// <see cref="ExecutionCoordinator.DispatchNextAsync"/>'s own identical, already-disclosed
    /// WP-024 finding): <see cref="ActionRequest"/>/<see cref="RollbackRequestedPayload"/>'s
    /// <c>ActionId</c> is a fresh, ephemeral value with no persisted mapping back to any
    /// <see cref="DispatchedTask.TaskId"/> anywhere in this codebase — nothing at dispatch time
    /// records which ActionId corresponds to which Task. Resolving a Task from a bare
    /// RollbackRequested event is therefore not currently possible without either extending the
    /// frozen ActionRequest contract (out of WP-025.3's authorized scope — "Do not modify the
    /// Protection Layer") or inventing a new correlation store (forbidden — no new stores without
    /// an explicit frozen requirement). This handler observes and records the request honestly;
    /// it intentionally does not fabricate task resolution. Real rollback execution for a
    /// concretely known Task is exposed via <see cref="RollbackAsync(DispatchedTask, CancellationToken)"/>,
    /// usable by any caller that already has the Task in hand (e.g. Workflow Recovery, §15.8).
    /// </summary>
    public (Guid ActionId, string OwningSubsystem, string Reason)? LastRollbackRequestObserved { get; private set; }

    public void OnRollbackRequested(Guid actionId, string owningSubsystem, string reason) =>
        LastRollbackRequestObserved = (actionId, owningSubsystem, reason);

    /// <summary>
    /// Executes <paramref name="task"/>'s Constitution Part 6 §6.2 Rollback Path: persists the
    /// target state, then — only after that write has committed — publishes
    /// <c>RollbackExecuted</c>. Throws <see cref="NotSupportedException"/> (no persistence, no
    /// event) for a source state with no single, defined Rollback Path (FR-PE9's own anticipated
    /// fallback: such transitions are "simply non-rollback-able by design," Recovery Planning
    /// falls back to Compensation Actions instead, §19.5/§19.4).
    /// </summary>
    public async Task<DispatchedTask> RollbackAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        var targetState = ResolveRollbackTargetState(task);

        var rolledBack = task with { State = targetState };
        await store.UpsertAsync(rolledBack, cancellationToken);

        // Persist-then-publish: RollbackExecuted is a factual statement that the rollback
        // transition already succeeded — published only after the write above has committed.
        rollbackExecutedEventPublisher.PublishRollbackExecuted(rolledBack.TaskId, targetState.ToString());

        return rolledBack;
    }

    /// <summary>
    /// PE §19.2: "Workflow-level rollback... expressed as the ordered application of each
    /// constituent Task's own existing Rollback Path, never a new mechanism." Executes
    /// sequentially; on the first failure, the exception propagates immediately and no further
    /// Task in <paramref name="orderedTasks"/> is attempted — already-succeeded Tasks are never
    /// compensated, and no workflow-level "all succeeded" event is ever published (only the
    /// per-Task <c>RollbackExecuted</c> events already emitted by the successful iterations).
    /// </summary>
    public async Task<IReadOnlyList<DispatchedTask>> RollbackAsync(
        IReadOnlyList<DispatchedTask> orderedTasks, CancellationToken cancellationToken = default)
    {
        var rolledBack = new List<DispatchedTask>(orderedTasks.Count);
        foreach (var task in orderedTasks)
        {
            rolledBack.Add(await RollbackAsync(task, cancellationToken));
        }

        return rolledBack;
    }

    /// <summary>
    /// Constitution Part 6 §6.2's Rollback Path column, table-driven, never inferred.
    /// <see cref="TaskLifecycleState.Running"/> is table-ambiguous by source *transition*, not
    /// source *state*: <c>Ready → Running</c> rolls back to <c>Ready</c>, but
    /// <c>Retry → Running</c> rolls back to <c>Blocked</c> — <see cref="DispatchedTask"/> records
    /// no "which transition got me here" field. <see cref="DispatchedTask.RetryCount"/> (WP-025.1)
    /// is reused to disambiguate: only <c>RetryManager</c>'s <c>Retry → Running</c> write ever
    /// increments it while setting <see cref="TaskLifecycleState.Running"/>, so
    /// <c>RetryCount &gt; 0</c> reliably indicates arrival via that transition. This is a
    /// disclosed, minimal interpretation of already-persisted state, not a frozen requirement
    /// stated by any document.
    /// <see cref="TaskLifecycleState.Blocked"/> is irreducibly two-valued in the table itself
    /// ("→ Running (after fix) or → Cancelled") with no field anywhere recording whether "a fix"
    /// was applied — refused rather than guessed, per FR-PE9's own anticipated fallback.
    /// </summary>
    private static TaskLifecycleState ResolveRollbackTargetState(DispatchedTask task) => task.State switch
    {
        TaskLifecycleState.Planned => TaskLifecycleState.Cancelled,
        TaskLifecycleState.Ready => TaskLifecycleState.Planned,
        TaskLifecycleState.Running when task.RetryCount > 0 => TaskLifecycleState.Blocked,
        TaskLifecycleState.Running => TaskLifecycleState.Ready,
        TaskLifecycleState.Waiting => TaskLifecycleState.Running,
        TaskLifecycleState.Review => TaskLifecycleState.Running,
        TaskLifecycleState.Testing => TaskLifecycleState.Review,
        TaskLifecycleState.Verified => TaskLifecycleState.Testing,
        TaskLifecycleState.Released => TaskLifecycleState.Verified,
        TaskLifecycleState.Blocked => throw new NotSupportedException(
            $"Task '{task.TaskId}': Constitution Part 6 §6.2's Running → Blocked Rollback Path is "
            + "two-valued (\"→ Running (after fix) or → Cancelled\") with no field on DispatchedTask "
            + "able to determine which applies. This is a genuine, disclosed architecture ambiguity "
            + "(FR-PE9's own anticipated fallback: non-rollback-able by design, Recovery Planning "
            + "falls back to Compensation Actions instead, PE §19.4/§19.5) — not implementable "
            + "without an Architecture Board ruling."),
        _ => throw new NotSupportedException(
            $"Task '{task.TaskId}': Constitution Part 6 §6.2 defines no Rollback Path for source "
            + $"state '{task.State}'."),
    };
}
