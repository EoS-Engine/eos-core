using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.6: unchanged from Constitution Part 7 in
/// structure (Priority Queue, Dependency Graph, Resource/Concurrency budgets, §7.2) and algorithm
/// (§7.3) — this class is that unchanged algorithm's real implementation, plus §14's Scheduling
/// execution-mode detail. Materializes <see cref="DispatchedTask"/> rows (Constitution Part 6's
/// Task entity, first realized here) from a WP-023 <see cref="Plan"/> once <c>PlannerGenerated</c>
/// (§20) announces it, then drives <see cref="TaskLifecycleState.Planned"/> →
/// <see cref="TaskLifecycleState.Ready"/> and selects the next dispatchable Task for the
/// Execution Coordinator (§10.7) — never itself performs the Protection-gated
/// <see cref="TaskLifecycleState.Ready"/> → <see cref="TaskLifecycleState.Running"/> transition
/// (FR-PE1/FR-PE2, the Execution Coordinator's sole responsibility).
/// </summary>
public sealed class Scheduler(
    DispatchedTaskStore store,
    IPlanQueryClient planQueryClient,
    IGoalPlanQueryClient goalPlanQueryClient,
    IResourceManagementClient resourceManagementClient,
    int concurrencyCeiling,
    int dailyCapacity)
{
    private readonly Lock _pendingPriorityLock = new();
    private readonly Dictionary<Guid, int> _pendingPriorityByTaskId = [];

    /// <summary>
    /// <c>TaskCreated</c> (§20: "task_id, competencies_required, priority") always precedes
    /// <c>PlannerGenerated</c> for the same Plan (WP-023's <c>PlanningEngine.SubmitGoalAsync</c>
    /// publishes every Task's <c>TaskCreated</c>, then <c>PlannerGenerated</c>, last) — caching by
    /// <c>TaskId</c> here lets <see cref="OnPlannerGeneratedAsync"/> carry Planner's own computed
    /// priority (Constitution Part 7 §7.2) onto each materialized <see cref="DispatchedTask"/>
    /// without recomputing it (a second, drifting copy of <c>PriorityManager</c>'s logic) or
    /// requiring a shared <c>correlation_id</c> WP-023's publishers don't supply.
    /// </summary>
    public void OnTaskCreated(Guid taskId, int priority)
    {
        lock (_pendingPriorityLock)
        {
            _pendingPriorityByTaskId[taskId] = priority;
        }
    }

    /// <summary>
    /// Materializes one <see cref="DispatchedTask"/> per <see cref="PlanTask"/> in the given
    /// Plan, in <see cref="TaskLifecycleState.Created"/> then immediately
    /// <see cref="TaskLifecycleState.Planned"/> (Constitution Part 6 §6.2's "Created → Planned"
    /// transition). Every Task defaults to <see cref="SchedulingMode.Immediate"/> (§14: "the
    /// default case") — no upstream signal anywhere in this codebase yet expresses an intended
    /// Scheduling mode for a WP-023-produced Task; the other six modes are real, tested (see
    /// <c>SchedulerTests</c>) and reachable via <see cref="ScheduleAsync"/> directly, a disclosed
    /// limitation matching this WP's own established convention for gaps of this kind.
    ///
    /// Constitution Part 6 §6.2 names "EOS.Planner" as this transition's Allowed Actor —
    /// <c>EOS.Planner</c> is the architectural actor/evidence producer (a validated, decomposed
    /// Task Graph is exactly the "Backlog item + competency match" evidence that column names),
    /// but <c>EOS.Orchestrator</c> mechanically performs the persistence write here because
    /// Constitution §1.2's frozen dependency boundary makes the reverse impossible: EOS.Planner
    /// cannot reference EOS.Orchestrator, and WP-023's own <c>PlanTask</c> is deliberately
    /// planning-time-only, never becoming Constitution Part 6's real Task entity itself. The
    /// <c>PlannerGenerated</c> event (§20) is the authorization/evidence boundary between the two
    /// — the Scheduler only ever materializes a Task once EOS.Planner has already emitted it. This
    /// does not introduce a new architectural ownership model, only a mechanical division of
    /// labor across an already-frozen project boundary, mirroring ADR-PE003's identical pattern of
    /// resolving a literal-text-vs-practical-mechanics tension via disclosed interpretation.
    ///
    /// All tasks materialized by one call are all-or-nothing: a Task Graph's tasks may depend on
    /// each other (<c>DependsOnTaskIds</c>), so a partially-materialized graph would leave
    /// dependent tasks permanently unable to satisfy a dependency that will never exist. If any
    /// task in this call fails to reach <see cref="TaskLifecycleState.Planned"/> — including
    /// cancellation — every task this call already wrote is transitioned to
    /// <see cref="TaskLifecycleState.Cancelled"/> (Constitution's own universal "Any → Cancelled"
    /// rule, §6.2, applied at the Task level for the first time — not a new state or mechanism)
    /// using <see cref="CancellationToken.None"/> for that compensating write specifically (reusing
    /// the already-cancelled token here would make the compensation itself fail immediately,
    /// exactly the pitfall already found and fixed in WP-023's <c>PlanningEngine.SubmitGoalAsync</c>
    /// for the identical class of bug), and the original exception is rethrown unmodified — never
    /// swallowed, never converted into a false success. This guarantees no task is ever left
    /// permanently invisible in <see cref="TaskLifecycleState.Created"/> (nothing anywhere in this
    /// class ever reads that state), and the caller (ultimately WP-023's
    /// <c>PlanningEngine.SubmitGoalAsync</c>, via the synchronous <c>PlannerGenerated</c> event
    /// handler) receives an honest failure signal rather than a silently corrupted, unreachable
    /// Task Graph.
    /// </summary>
    public async Task OnPlannerGeneratedAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await planQueryClient.GetByIdAsync(planId, cancellationToken)
            ?? throw new InvalidOperationException($"Plan '{planId}' does not exist.");

        var materializedTaskIds = new List<Guid>();
        try
        {
            foreach (var planTask in plan.Tasks)
            {
                int priority;
                lock (_pendingPriorityLock)
                {
                    if (!_pendingPriorityByTaskId.Remove(planTask.TaskId, out priority))
                    {
                        throw new InvalidOperationException(
                            $"Task '{planTask.TaskId}' has no cached priority — TaskCreated must be observed before PlannerGenerated.");
                    }
                }

                var dispatchedTask = new DispatchedTask(
                    TaskId: planTask.TaskId,
                    PlanId: plan.PlanId,
                    GoalId: plan.GoalId,
                    Description: planTask.Description,
                    CompetencyRequirements: planTask.CompetencyRequirements,
                    DependsOnTaskIds: planTask.DependsOnTaskIds,
                    Priority: priority,
                    State: TaskLifecycleState.Created,
                    SchedulingMode: SchedulingMode.Immediate,
                    NotBefore: null,
                    RunningAt: null,
                    EventObserved: false,
                    RetryCount: 0,
                    BlockedReason: null);

                await store.UpsertAsync(dispatchedTask, cancellationToken);
                materializedTaskIds.Add(dispatchedTask.TaskId);

                await store.UpsertAsync(dispatchedTask with { State = TaskLifecycleState.Planned }, cancellationToken);
            }
        }
        catch
        {
            foreach (var taskId in materializedTaskIds)
            {
                var stuckTask = await store.GetByIdAsync(taskId, CancellationToken.None);
                if (stuckTask is not null && stuckTask.State is TaskLifecycleState.Created or TaskLifecycleState.Planned)
                {
                    await store.UpsertAsync(stuckTask with { State = TaskLifecycleState.Cancelled }, CancellationToken.None);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Materializes a single <see cref="DispatchedTask"/> for a non-default Scheduling mode
    /// (§14) — the entry point <see cref="OnPlannerGeneratedAsync"/> itself never uses today
    /// (disclosed above), exercised directly by <c>SchedulerTests</c>.
    /// </summary>
    public async Task ScheduleAsync(DispatchedTask task, CancellationToken cancellationToken)
    {
        await store.UpsertAsync(task with { State = TaskLifecycleState.Created }, cancellationToken);
        await store.UpsertAsync(task with { State = TaskLifecycleState.Planned }, cancellationToken);
    }

    /// <summary>
    /// §14's Event-Driven Execution: "Task becomes eligible only upon a specific Event Catalog
    /// event." No generic, string-keyed Event Catalog subscription mechanism exists anywhere in
    /// this codebase today (<c>EventMediator.Subscribe</c> is compile-time payload-typed, not
    /// event-name-keyed) — this method is the minimal honest representation available: whichever
    /// caller knows the required event has genuinely occurred marks it explicitly, matching the
    /// same manual, caller-driven pattern <see cref="ScheduleAsync"/> already establishes for
    /// every non-default Scheduling mode. Until this is called, an Event-Driven Task's
    /// <see cref="DispatchedTask.EventObserved"/> stays <c>false</c> and it is never eligible —
    /// never silently treated as <see cref="SchedulingMode.Immediate"/>.
    /// </summary>
    public async Task MarkEventObserved(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await store.GetByIdAsync(taskId, cancellationToken)
            ?? throw new InvalidOperationException($"Task '{taskId}' does not exist.");

        await store.UpsertAsync(task with { EventObserved = true }, cancellationToken);
    }

    /// <summary>
    /// Constitution Part 6 §6.2's "Planned → Ready" transition: "Evidence Required: Resource
    /// budget available (Part 7)" is re-checked at actual dispatch time (§7.3 steps 3-4, since
    /// resource/concurrency conditions are inherently time-volatile — "on failure to satisfy any
    /// check, task remains Ready and is re-evaluated next micro-cycle" only makes sense as a
    /// repeatable check, not a one-time gate); this transition's own "Gates: Dependency graph
    /// satisfied" is the one condition checked here, per §13.3: a Task becomes Ready only when
    /// every dependency has reached <see cref="TaskLifecycleState.Verified"/> or
    /// <see cref="TaskLifecycleState.Released"/>. WP-025.4 adds the current-Plan check
    /// (<see cref="IsCurrentPlanAsync"/>) as a second, additive gate on this same transition — an
    /// old-Plan Task is left exactly as-is in <see cref="TaskLifecycleState.Planned"/>, never
    /// promoted to Ready, since promoting it would itself be a mutation the WP-025 Architecture
    /// Board's Q2 ruling forbids ("old Planned/Ready tasks remain persisted and unchanged").
    /// </summary>
    public async Task<IReadOnlyList<DispatchedTask>> EvaluateReadinessAsync(CancellationToken cancellationToken)
    {
        var planned = await store.GetByStateAsync(TaskLifecycleState.Planned, cancellationToken);
        var readyNow = new List<DispatchedTask>();

        foreach (var task in planned)
        {
            if (!await AreDependenciesSatisfiedAsync(task, cancellationToken))
            {
                continue;
            }

            if (!await IsCurrentPlanAsync(task, cancellationToken))
            {
                continue;
            }

            var ready = task with { State = TaskLifecycleState.Ready };
            await store.UpsertAsync(ready, cancellationToken);
            readyNow.Add(ready);
        }

        return readyNow;
    }

    /// <summary>
    /// §7.3 steps 1, 3, 4: pulls <see cref="TaskLifecycleState.Ready"/> tasks ordered by
    /// Priority Queue (step 1), returning the first one whose Scheduling-mode eligibility
    /// (§14), Resource Budget headroom (step 3), and Concurrency ceiling (step 4) all currently
    /// pass — or <c>null</c> if none is eligible this cycle. Deterministic given the same store
    /// state and budget/policy state (FR-PE3) — no Reasoning Engine involvement (§10.11 is
    /// Planning-time only).
    ///
    /// Known limitation, not fixed here: this read (via <see cref="DispatchedTaskStore"/>) and
    /// <see cref="ExecutionCoordinator"/>'s later write of the selected Task to
    /// <see cref="TaskLifecycleState.Running"/> are not atomic — no conditional/claim-style
    /// update guards the gap between them. Two concurrent callers of
    /// <see cref="ExecutionCoordinator.DispatchNextAsync"/> could select and dispatch the same
    /// Task. This is not fixed with a new concurrency mechanism (transaction, lock, or optimistic
    /// version check) because nothing in this codebase's Composition Root currently invokes
    /// <c>DispatchNextAsync</c> from more than one place — introducing one now would be
    /// speculative infrastructure for a caller that doesn't exist yet. This method makes no claim,
    /// implicit or otherwise, of being safe under concurrent invocation.
    /// </summary>
    public async Task<DispatchedTask?> SelectNextDispatchableTaskAsync(CancellationToken cancellationToken)
    {
        var readyTasks = await store.GetReadyTasksOrderedByPriorityAsync(cancellationToken);
        if (readyTasks.Count == 0)
        {
            return null;
        }

        if (!ResourceBudgetHasHeadroom())
        {
            return null;
        }

        var runningCount = await store.CountByStateAsync(TaskLifecycleState.Running, cancellationToken);
        if (runningCount >= concurrencyCeiling)
        {
            return null;
        }

        var runningToday = await store.CountRunningSinceAsync(DateTimeOffset.UtcNow.AddDays(-1), cancellationToken);
        if (runningToday >= dailyCapacity)
        {
            return null;
        }

        foreach (var task in readyTasks)
        {
            if (!IsEligibleForItsSchedulingMode(task))
            {
                continue;
            }

            if (!await IsCurrentPlanAsync(task, cancellationToken))
            {
                continue;
            }

            return task;
        }

        return null;
    }

    /// <summary>
    /// WP-025 Architecture Board Ruling Q1/Q2 (frozen): a Task is dispatchable only when
    /// <c>DispatchedTask.PlanId == Goal.PlanId</c> — <see cref="Plan"/>s are immutable/versioned
    /// (Dynamic Replanning creates a new Plan artifact, never mutates the old one), and the Goal's
    /// <c>PlanId</c> pointer is the sole current-Plan indicator (no <c>IsSuperseded</c>/
    /// <c>IsActive</c>/<c>PlanStatus</c> field, no new lifecycle state). An old-Plan Task is left
    /// persisted exactly as-is — this check only ever affects *selection*, never mutates the Task
    /// or cancels it. A missing Goal/unset <c>Goal.PlanId</c> is treated identically to "not the
    /// current Plan" (not dispatchable) rather than fabricating an eligible fallback — genuine
    /// query failures/cancellation are not caught here and propagate to the caller unmodified.
    /// </summary>
    private async Task<bool> IsCurrentPlanAsync(DispatchedTask task, CancellationToken cancellationToken)
    {
        var currentPlanId = await goalPlanQueryClient.GetCurrentPlanIdAsync(task.GoalId, cancellationToken);
        return currentPlanId is not null && task.PlanId == currentPlanId;
    }

    // §7.3 step 3: "Check Resource Budget headroom (CPU/RAM/Inference)." IResourceManagementClient
    // (WP-021/022) reports a live sample and a Safe/Warning/Critical/Emergency tier for it — this
    // reuses WP-022's own BackgroundTaskController precedent of checking tier rather than raw
    // arithmetic against a ceiling: Safe/Warning permit dispatch, Critical/Emergency defer it.
    private bool ResourceBudgetHasHeadroom()
    {
        ResourceType[] monitoredResourceTypes = [ResourceType.Cpu, ResourceType.Ram, ResourceType.ModelUsage];
        foreach (var resourceType in monitoredResourceTypes)
        {
            var tier = resourceManagementClient.GetCurrentTier(resourceType);
            if (tier is CapacityTier.Critical or CapacityTier.Emergency)
            {
                return false;
            }
        }

        return true;
    }

    // §14's table: modes differ only in eligibility timing, never in dispatch mechanics.
    // Background/Idle-Time Execution's Maintenance Window condition reuses WP-022's own disclosed
    // BackgroundTaskController.WithinMaintenanceWindow precedent (Implementation Plan Decision
    // D10) rather than inventing new Maintenance Window infrastructure this codebase doesn't have.
    // Periodic Execution's "fixed cadence" (§14) is, at bottom, the same timestamp-gate mechanism
    // §14 already gives Delayed/Scheduled ("ineligible... until that time passes") — folded into
    // the same NotBefore check rather than inventing a second, parallel cadence field; each
    // occurrence is materialized as its own DispatchedTask (consistent with §14's own "a new
    // TaskCreated... on a fixed cadence" framing), carrying whatever NotBefore its own caller
    // supplies via ScheduleAsync. Event-Driven Execution's "eligible only upon a specific Event
    // Catalog event" has no reusable existing field — see DispatchedTask.EventObserved and
    // Scheduler.MarkEventObserved's own doc comments for why a task is never treated as eligible
    // until something explicitly observes the required event.
    private static bool IsEligibleForItsSchedulingMode(DispatchedTask task) => task.SchedulingMode switch
    {
        SchedulingMode.Immediate => true,
        SchedulingMode.Delayed or SchedulingMode.Scheduled or SchedulingMode.Periodic =>
            task.NotBefore is null || task.NotBefore <= DateTimeOffset.UtcNow,
        SchedulingMode.Background or SchedulingMode.IdleTime => WithinMaintenanceWindow(),
        SchedulingMode.EventDriven => task.EventObserved,
        _ => throw new ArgumentOutOfRangeException(nameof(task), task.SchedulingMode, "Unsupported SchedulingMode."),
    };

    // WP-022 Implementation Plan Decision D10: no Maintenance-Window data source exists anywhere
    // in this codebase to legally consult — disclosed identically to BackgroundTaskController's
    // own identical stub, not a fabricated business rule.
    private static bool WithinMaintenanceWindow() => true;

    private async Task<bool> AreDependenciesSatisfiedAsync(DispatchedTask task, CancellationToken cancellationToken)
    {
        foreach (var dependsOnTaskId in task.DependsOnTaskIds)
        {
            var dependency = await store.GetByIdAsync(dependsOnTaskId, cancellationToken);
            if (dependency is null || dependency.State is not (TaskLifecycleState.Verified or TaskLifecycleState.Released))
            {
                return false;
            }
        }

        return true;
    }
}
