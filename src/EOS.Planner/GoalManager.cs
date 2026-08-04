using EOS.Contracts;

namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.1a: owns the Goal lifecycle (§11.1) and
/// hierarchy (§11.2) — the entity Constitution §0.4.1 refers to only as "Backlog items." Does
/// not itself decompose a Goal into Tasks (the Task Graph Builder's job, §10.3) — only manages
/// the Goal's own state and its relationship to sibling/child Goals.
/// </summary>
public sealed class GoalManager(
    GoalStore goalStore,
    IGoalCreatedEventPublisher goalCreatedEventPublisher,
    IGoalCancelledEventPublisher goalCancelledEventPublisher)
{
    /// <summary>
    /// Persists <paramref name="submittedGoal"/> as a brand-new Goal, respecting its
    /// caller-assigned <see cref="Goal.GoalId"/> (matching this codebase's established
    /// convention of callers generating an entity's id upfront, e.g. <c>ActionRequest</c> —
    /// never silently replaced with a different, system-generated one) while forcing
    /// <see cref="GoalLifecycleState.Proposed"/> and a null <see cref="Goal.PlanId"/>, since
    /// those are guaranteed values for any newly-submitted Goal regardless of what the caller
    /// supplied.
    /// </summary>
    public async Task<Goal> CreateGoalAsync(Goal submittedGoal, CancellationToken cancellationToken)
    {
        var goal = submittedGoal with { State = GoalLifecycleState.Proposed, PlanId = null };

        await goalStore.UpsertAsync(goal, cancellationToken);
        goalCreatedEventPublisher.PublishGoalCreated(goal.GoalId, goal.ParentGoalId, goal.Statement);

        return goal;
    }

    public Task<Goal?> GetByIdAsync(Guid goalId, CancellationToken cancellationToken) =>
        goalStore.GetByIdAsync(goalId, cancellationToken);

    public async Task<Goal> TransitionStateAsync(Goal goal, GoalLifecycleState newState, CancellationToken cancellationToken)
    {
        var updated = goal with { State = newState };
        await goalStore.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<Goal> AttachPlanAsync(Goal goal, Guid planId, CancellationToken cancellationToken)
    {
        var updated = goal with { State = GoalLifecycleState.Planned, PlanId = planId };
        await goalStore.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    /// <summary>
    /// §11.6: "Mirrors Constitution Part 6 §6.2's 'Any → Cancelled' Task transition rule, applied
    /// at the Goal level: cancelling a Goal cancels every incomplete descendant Task via the
    /// existing Task Lifecycle rule, never a new bespoke cancellation mechanism." Cascades
    /// through the Goal Hierarchy (§11.2) first — every descendant Goal transitions to
    /// <see cref="GoalLifecycleState.Cancelled"/> alongside the target Goal. Per-Task
    /// cancellation itself is Constitution Part 6 §6.2's own existing, unchanged actor-gated
    /// transition, applied once the Scheduler (§10.6, WP-024) has actually dispatched a Goal's
    /// Tasks — WP-023 produces only planning-time <see cref="PlanTask"/> records, never a
    /// dispatched Task Lifecycle entity, so there is nothing yet for this cascade to reach below
    /// the Goal level.
    /// </summary>
    public async Task CancelGoalAsync(Guid goalId, string reason, CancellationToken cancellationToken)
    {
        var goal = await goalStore.GetByIdAsync(goalId, cancellationToken)
            ?? throw new InvalidOperationException($"Goal '{goalId}' does not exist.");

        await CancelGoalAndDescendantsAsync(goal, reason, cancellationToken);
    }

    private async Task CancelGoalAndDescendantsAsync(Goal goal, string reason, CancellationToken cancellationToken)
    {
        var children = await goalStore.GetChildrenAsync(goal.GoalId, cancellationToken);
        foreach (var child in children)
        {
            await CancelGoalAndDescendantsAsync(child, reason, cancellationToken);
        }

        var cancelled = goal with { State = GoalLifecycleState.Cancelled };
        await goalStore.UpsertAsync(cancelled, cancellationToken);
        goalCancelledEventPublisher.PublishGoalCancelled(goal.GoalId, reason);
    }
}
