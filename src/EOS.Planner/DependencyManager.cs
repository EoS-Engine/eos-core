using EOS.Contracts;

namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.4: maintains Goal-level dependencies
/// (§11.4) — "distinct from and layered above the unchanged Task-level Dependency Graph"
/// (Constitution Part 7 §7.2), which <see cref="TaskGraphBuilder"/>'s own
/// <c>PlanTask.DependsOnTaskIds</c> already realizes unchanged.
///
/// <see cref="GetDependentGoalCountAsync"/> is the one method <see cref="PlanningEngine"/> calls
/// today (feeding §11.3's "aggregate priority of dependent Goals" into
/// <see cref="PriorityManager"/>). <see cref="AddGoalDependencyAsync"/> and
/// <see cref="AreDependenciesSatisfiedAsync"/> have no caller within WP-023's own scope: neither
/// <c>Goal</c> (§11 — this WP's own record) nor <c>IPlanningClient</c> (§21.1's frozen
/// three-method surface, submit/status/cancel only) carries a place for a caller to ever supply
/// "Goal A depends on Goal B" (§11.4), and inferring such an edge automatically from Task Graph
/// content is not something the Task Graph Builder (§10.3) does. This is a disclosed, honest gap
/// (matching this WP's established convention, e.g. <c>GoalValidator.IsCompetencyFeasible</c>) —
/// the store-level mechanism these two methods provide is ready for the Scheduler (§10.6, WP-024,
/// the component that actually needs to gate on Goal-level dependency satisfaction) or a future
/// dependency-declaration entry point to call, not dead in the sense of never becoming reachable.
/// </summary>
public sealed class DependencyManager(GoalDependencyStore goalDependencyStore, GoalStore goalStore)
{
    public Task AddGoalDependencyAsync(Guid goalId, Guid dependsOnGoalId, CancellationToken cancellationToken) =>
        goalDependencyStore.AddDependencyAsync(goalId, dependsOnGoalId, cancellationToken);

    /// <summary>
    /// §11.3's "aggregate priority of dependent Goals" input to the Priority Manager (§10.5) —
    /// the count of other Goals whose own §11.4 dependency points at <paramref name="goalId"/>.
    /// </summary>
    public async Task<int> GetDependentGoalCountAsync(Guid goalId, CancellationToken cancellationToken)
    {
        var dependents = await goalDependencyStore.GetDependentsAsync(goalId, cancellationToken);
        return dependents.Count;
    }

    /// <summary>
    /// §11.4: a Goal depends on another Goal "if any Task in [its] eventual Task Graph requires
    /// evidence or capability only available once [the other] completes" — satisfied once every
    /// depended-upon Goal has reached <see cref="GoalLifecycleState.Completed"/>.
    /// </summary>
    public async Task<bool> AreDependenciesSatisfiedAsync(Guid goalId, CancellationToken cancellationToken)
    {
        var dependsOnGoalIds = await goalDependencyStore.GetDependenciesAsync(goalId, cancellationToken);

        foreach (var dependsOnGoalId in dependsOnGoalIds)
        {
            var dependency = await goalStore.GetByIdAsync(dependsOnGoalId, cancellationToken);
            if (dependency is null || dependency.State != GoalLifecycleState.Completed)
            {
                return false;
            }
        }

        return true;
    }
}
