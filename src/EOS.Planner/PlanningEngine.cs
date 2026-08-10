using EOS.Contracts;

namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.2: the concrete realization of Constitution
/// §0.4's Capability Planner. Orchestrates <see cref="GoalManager"/>, <see cref="GoalValidator"/>,
/// <see cref="TaskGraphBuilder"/>, <see cref="DependencyManager"/>, and
/// <see cref="PriorityManager"/> into <see cref="IPlanningClient"/>'s implementation — the
/// Planning Engine alone finalizes every <c>Plan</c> artifact (§10.11/ADR-PE003).
/// </summary>
public sealed class PlanningEngine(
    GoalManager goalManager,
    GoalValidator goalValidator,
    TaskGraphBuilder taskGraphBuilder,
    DependencyManager dependencyManager,
    PriorityManager priorityManager,
    PlanStore planStore,
    ITaskCreatedEventPublisher taskCreatedEventPublisher,
    IPlannerGeneratedEventPublisher plannerGeneratedEventPublisher,
    IReplanTriggeredEventPublisher replanTriggeredEventPublisher) : IPlanningClient
{
    public async Task<Plan> SubmitGoalAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        var created = await goalManager.CreateGoalAsync(goal, cancellationToken);

        var validation = goalValidator.Validate(created);
        if (!validation.Feasible)
        {
            // Same §11.6/§22.1 "Cancelled (from any state)" resolution as the try/catch below —
            // otherwise the just-created Goal is left stuck in Proposed forever, and validation
            // failure is the more frequent of the two outcomes this method can fail on.
            await goalManager.CancelGoalAsync(created.GoalId, $"Goal failed validation: {validation.Reason}", CancellationToken.None);
            throw new InvalidOperationException($"Goal '{created.GoalId}' failed validation: {validation.Reason}");
        }

        var validated = await goalManager.TransitionStateAsync(created, GoalLifecycleState.Validated, cancellationToken);
        var decomposing = await goalManager.TransitionStateAsync(validated, GoalLifecycleState.Decomposing, cancellationToken);

        // §22.1 has no "failed decomposition"/"failed persistence" state, and everything from here
        // through AttachPlanAsync is real external I/O (Knowledge/Reasoning via TaskGraphBuilder,
        // then SQL Server via PlanStore/GoalStore) that can genuinely fail. Rather than leaving the
        // Goal stuck in Decomposing indefinitely — or a Plan row persisted with no Goal ever
        // reaching Planned — resolve any failure in this segment via the already-defined
        // §11.6/§22.1 "Cancelled (from any state)" transition. No new state, and no cross-store SQL
        // transaction spanning PlanStore/GoalStore, is introduced — no such mechanism exists
        // anywhere else in this codebase, and this compensation achieves the same practical
        // outcome (no Goal left permanently stuck) without one.
        PlanTask[] tasks;
        Plan plan;
        int priority;
        try
        {
            tasks = await taskGraphBuilder.DecomposeAsync(decomposing, cancellationToken);

            // §11.4/§10.4: dependency criticality input to the Priority Manager (§11.3) — the
            // count of other Goals depending on this one, via the Dependency Manager's own
            // tracking.
            var dependentGoalCount = await dependencyManager.GetDependentGoalCountAsync(decomposing.GoalId, cancellationToken);

            // Constitution §0.4.2 defines "estimated resource cost" as a CPU/RAM/inference-budget
            // quantity (Part 7) — no such measurement is available to EOS.Planner (Constitution
            // §1.2's dependency row for EOS.Planner does not include EOS.Resources, so no budget
            // source can be consulted here). Task count is used as the disclosed, deterministic
            // stand-in until a real budget estimator exists, matching this WP's established
            // convention of disclosing every non-spec-defined judgment call (e.g. PriorityManager,
            // GoalValidator.IsCompetencyFeasible) rather than fabricating one silently.
            var estimatedResourceCost = tasks.Length;
            priority = priorityManager.ComputePriority(dependentGoalCount, estimatedResourceCost);

            plan = new Plan(
                PlanId: Guid.NewGuid(),
                GoalId: decomposing.GoalId,
                Tasks: tasks,
                EstimatedResourceCost: estimatedResourceCost,
                // Constitution §0.4.2 names this output but defines no formula for it. Disclosed,
                // deterministic heuristic: more decomposed steps imply proportionally less
                // confidence in the plan as a whole, in the absence of any frozen document
                // specifying one.
                RiskAdjustedConfidenceScore: tasks.Length > 0 ? 1.0 / tasks.Length : 0.0,
                PreviousPlanId: null);

            await planStore.InsertAsync(plan, cancellationToken);
            await goalManager.AttachPlanAsync(decomposing, plan.PlanId, cancellationToken);
        }
        catch
        {
            // Best-effort compensation must still run even when the triggering failure was the
            // caller cancelling `cancellationToken` itself — reusing that same (already-cancelled)
            // token here would make this call fail immediately, leaving the Goal stuck and
            // replacing the original exception with a fresh cancellation one.
            await goalManager.CancelGoalAsync(decomposing.GoalId, "Goal decomposition or plan persistence failed.", CancellationToken.None);
            throw;
        }

        // Published only after the Goal has actually reached Planned above — consumers (the
        // Scheduler, WP-024) must never observe TaskCreated for a Goal that could still fail to
        // reach a committed Planned state.
        foreach (var task in tasks)
        {
            taskCreatedEventPublisher.PublishTaskCreated(task.TaskId, task.CompetencyRequirements, priority);
        }

        // §20/Constitution Part 3: PlannerGenerated's payload is (plan_id, task_graph_ref) as two
        // named fields, but WP-023 Implementation Blueprint Decision D3 deliberately made Plan a
        // single record rather than Plan wrapping a separately-persisted Task Graph object — so
        // no distinct task_graph_ref identity exists to supply. plan.PlanId is passed for both,
        // which is the direct consequence of D3, not an oversight.
        plannerGeneratedEventPublisher.PublishPlannerGenerated(plan.PlanId, plan.PlanId);

        return plan;
    }

    public async Task<GoalStatus> GetGoalStatusAsync(string goalId, CancellationToken cancellationToken = default)
    {
        var id = Guid.Parse(goalId);
        var goal = await goalManager.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Goal '{goalId}' does not exist.");

        return new GoalStatus(goal.GoalId, goal.State, goal.PlanId);
    }

    public Task CancelGoalAsync(string goalId, string reason, CancellationToken cancellationToken = default) =>
        goalManager.CancelGoalAsync(Guid.Parse(goalId), reason, cancellationToken);

    /// <summary>
    /// WP-025.7: Planning-Execution-Engine-Specification-v1.0 §16.1 "Replanning After Failures" —
    /// triggered by a Task permanently <c>Blocked</c> (§13.7, realized by
    /// <c>EOS.Orchestrator.RetryManager</c>'s WP-025.2 exhaustion path). §16 intro: "produces a
    /// revised Plan artifact... through the same Planning Engine... always re-validated through
    /// Protection before resuming (FR-PE8)" — reuses the exact same
    /// <see cref="GoalValidator.Validate"/> Protection call <see cref="SubmitGoalAsync"/> already
    /// makes, the exact same decomposition/persistence/attach sequence, and the exact same
    /// <c>TaskCreated</c>/<c>PlannerGenerated</c> event pipeline the Scheduler (WP-024) already
    /// subscribes to for materializing a Plan's Tasks — no new mechanism for any of that.
    ///
    /// Constitution Part 8 §8.3's immutable-versioning rule is satisfied via
    /// <see cref="Plan.PreviousPlanId"/> (WP-025.1): the OLD Plan row and every
    /// <c>DispatchedTask</c> row that referenced it are never read, written, or referenced by
    /// this method at all — <c>EOS.Planner</c> has no dependency on <c>EOS.Orchestrator</c>'s
    /// <c>DispatchedTaskStore</c> (Constitution §1.2), so old-Plan Task rows are structurally
    /// impossible for this method to touch, matching the WP-025 Architecture Board's Q2 ruling
    /// ("old Planned/Ready tasks remain persisted and unchanged") by construction, not by a
    /// runtime check.
    ///
    /// Deliberately does NOT reuse <see cref="SubmitGoalAsync"/>'s catch-and-cancel-the-Goal
    /// compensation: that pattern exists to avoid leaving a brand-new Goal stuck in
    /// <c>Decomposing</c> forever. A Goal being replanned already has a valid, unaffected current
    /// Plan; cancelling it because a replan *attempt* failed would be a new, unauthorized side
    /// effect. On any failure here, the exception propagates unmodified and the Goal's existing
    /// <c>PlanId</c> is left exactly as it was (no write has occurred yet at the point of failure
    /// in every failure path below).
    /// </summary>
    public async Task<Plan> ReplanAfterFailureAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        var goal = await goalManager.GetByIdAsync(goalId, cancellationToken)
            ?? throw new InvalidOperationException($"Goal '{goalId}' does not exist.");

        // Constitution Part 6 §6.2/§11.6: "Any → Cancelled" is a terminal transition with no
        // defined path back out — no frozen document anywhere defines Cancelled → Planned or
        // Completed → Planned as legal. AttachPlanAsync unconditionally writes State = Planned,
        // so without this guard a Goal already given up on (Cancelled) or already finished
        // (Completed) would be silently resurrected. Checked before any decomposition,
        // persistence, or event publication — nothing below this point has executed yet.
        if (goal.State is GoalLifecycleState.Cancelled or GoalLifecycleState.Completed)
        {
            throw new InvalidOperationException(
                $"Goal '{goalId}' is {goal.State} and cannot be replanned — Constitution Part 6 §6.2 "
                + "defines no path back out of a terminal Goal state.");
        }

        var validation = goalValidator.Validate(goal);
        if (!validation.Feasible)
        {
            throw new InvalidOperationException($"Goal '{goalId}' failed re-validation during replanning: {validation.Reason}");
        }

        var tasks = await taskGraphBuilder.DecomposeAsync(goal, cancellationToken);

        var dependentGoalCount = await dependencyManager.GetDependentGoalCountAsync(goal.GoalId, cancellationToken);
        var estimatedResourceCost = tasks.Length;
        var priority = priorityManager.ComputePriority(dependentGoalCount, estimatedResourceCost);

        var revisedPlan = new Plan(
            PlanId: Guid.NewGuid(),
            GoalId: goal.GoalId,
            Tasks: tasks,
            EstimatedResourceCost: estimatedResourceCost,
            RiskAdjustedConfidenceScore: tasks.Length > 0 ? 1.0 / tasks.Length : 0.0,
            PreviousPlanId: goal.PlanId);

        await planStore.InsertAsync(revisedPlan, cancellationToken);
        await goalManager.AttachPlanAsync(goal, revisedPlan.PlanId, cancellationToken);

        foreach (var task in revisedPlan.Tasks)
        {
            taskCreatedEventPublisher.PublishTaskCreated(task.TaskId, task.CompetencyRequirements, priority);
        }

        plannerGeneratedEventPublisher.PublishPlannerGenerated(revisedPlan.PlanId, revisedPlan.PlanId);
        replanTriggeredEventPublisher.PublishReplanTriggered(goal.GoalId, "Failure");

        return revisedPlan;
    }
}
