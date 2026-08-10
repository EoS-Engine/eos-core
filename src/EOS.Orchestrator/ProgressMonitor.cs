using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §18: "Owned by the Progress Monitor (§10.8) — a
/// read-only observer, never a state-transition actor." Every method here only reads; none
/// writes, persists, or transitions a <see cref="DispatchedTask"/>.
///
/// §18.1 Task Status: "Directly reflects Constitution Part 6 §6.1's existing Task Lifecycle
/// states — the Progress Monitor introduces no new Task-level status vocabulary."
///
/// §18.2 Goal Status / §18.3 Workflow Status: "Aggregated from constituent Task statuses."
/// Aggregation is scoped to the Goal's *current* Plan only (WP-025 Architecture Board Ruling
/// Q1/Q2: <c>DispatchedTask.PlanId == Goal.PlanId</c>) via the existing
/// <see cref="IGoalPlanQueryClient"/> Composition Root Adapter (WP-025.4) — no new Goal-lookup
/// abstraction is introduced. Superseded-Plan rows are never cancelled, mutated, or excluded from
/// storage; they are simply not counted here, exactly matching
/// <see cref="Scheduler.EvaluateReadinessAsync"/>/<see cref="Scheduler.SelectNextDispatchableTaskAsync"/>'s
/// own WP-025.4 filtering precedent.
///
/// §18.6: "No second history store" (FR-PE10/ADR-PE002) — every computation below reads live,
/// current-state rows from the existing <see cref="DispatchedTaskStore"/>, never a persisted
/// progress counter/snapshot.
///
/// No "Workflow" entity is persisted anywhere in this codebase (confirmed by repository
/// inspection) — Constitution/PE-spec's Workflow concept is realized purely as a computed
/// grouping of a Goal's own <see cref="DispatchedTask"/> rows by <c>GoalId</c>
/// (<see cref="GetWorkflowProgressAsync"/> is a thin batch wrapper over
/// <see cref="GetGoalProgressAsync"/>, not a distinct entity or state machine).
///
/// Percent-complete uses PE §11.7's own frozen completion definition ("A Goal reaches Completed
/// only when every leaf Task in its Task Graph has reached Released or Archived"), applied
/// per-Task as the numerator — no weighting scheme, since none is defined by any frozen document.
/// </summary>
public sealed class ProgressMonitor(DispatchedTaskStore store, IGoalPlanQueryClient goalPlanQueryClient)
{
    private static readonly TaskLifecycleState[] CompleteStates =
        [TaskLifecycleState.Released, TaskLifecycleState.Archived];

    /// <summary>
    /// §18.1 Task Status — the Task's current lifecycle state, verbatim. <c>null</c> if no such
    /// Task exists (never fabricated).
    /// </summary>
    public async Task<TaskLifecycleState?> GetTaskProgressAsync(Guid taskId, CancellationToken cancellationToken = default) =>
        (await store.GetByIdAsync(taskId, cancellationToken))?.State;

    /// <summary>
    /// §18.2 Goal Status — current-Plan-filtered aggregation. A Goal with no known current Plan
    /// (e.g. never yet reached <see cref="GoalLifecycleState.Planned"/>) reports zero current
    /// Tasks — the same "not yet current" treatment
    /// <see cref="Scheduler.IsCurrentPlanAsync"/>-equivalent logic already applies, not a
    /// fabricated fallback. A genuine lookup failure (the <see cref="IGoalPlanQueryClient"/>
    /// itself throwing, or an already-cancelled token) propagates unmodified — never swallowed.
    /// </summary>
    public async Task<GoalProgress> GetGoalProgressAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        var currentPlanId = await goalPlanQueryClient.GetCurrentPlanIdAsync(goalId, cancellationToken);
        var allTasksForGoal = await store.GetByGoalIdAsync(goalId, cancellationToken);

        var currentPlanTasks = currentPlanId is null
            ? []
            : allTasksForGoal.Where(task => task.PlanId == currentPlanId).ToList();

        var countByState = currentPlanTasks
            .GroupBy(task => task.State)
            .ToDictionary(group => group.Key, group => group.Count());

        var completeCount = currentPlanTasks.Count(task => CompleteStates.Contains(task.State));
        var percentComplete = currentPlanTasks.Count == 0 ? 0.0 : 100.0 * completeCount / currentPlanTasks.Count;

        return new GoalProgress(goalId, currentPlanTasks.Count, countByState, percentComplete);
    }

    /// <summary>
    /// §18.3 Workflow Status — "Aggregated from constituent Task/step statuses... mirroring Task
    /// Lifecycle vocabulary at the Workflow level rather than inventing a divergent one." No
    /// persisted Workflow entity exists (or is created here); this is exactly
    /// <see cref="GetGoalProgressAsync"/>'s own computation, grouped by <c>GoalId</c>, for the
    /// given set of Goals.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, GoalProgress>> GetWorkflowProgressAsync(
        IReadOnlyCollection<Guid> goalIds, CancellationToken cancellationToken = default)
    {
        var progressByGoalId = new Dictionary<Guid, GoalProgress>();
        foreach (var goalId in goalIds)
        {
            progressByGoalId[goalId] = await GetGoalProgressAsync(goalId, cancellationToken);
        }

        return progressByGoalId;
    }
}
