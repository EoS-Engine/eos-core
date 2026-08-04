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
    IPlannerGeneratedEventPublisher plannerGeneratedEventPublisher) : IPlanningClient
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
                RiskAdjustedConfidenceScore: tasks.Length > 0 ? 1.0 / tasks.Length : 0.0);

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
}
