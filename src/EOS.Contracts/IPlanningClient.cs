namespace EOS.Contracts;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §21.1 (public, consumed by other subsystems).
/// Declares only the three methods whose backing functionality WP-023 itself builds — matching
/// the roadmap's own "Included components: IPlanningClient.submit_goal()" plus the Goal
/// Lifecycle/Cancellation functionality this WP's Goal Manager owns.
/// <c>query_generated_tasks</c> (serves Learning Engine, not yet built), <c>pause_workflow</c>,
/// and <c>resume_workflow</c> (govern Workflow state, §15.6/§15.7, Execution Coordinator's
/// domain, WP-024) are intentionally absent — no member declared, not a stubbed one — mirroring
/// the exact <c>query_history()</c>/AG-0003 precedent WP-020 already established for an
/// analogous compile-time omission. Async, matching <see cref="IReasoningEngineClient"/>'s own
/// shape (real I/O throughout: persistence, Memory consultation, bounded Reasoning delegation)
/// rather than <see cref="IProtectionClient"/>'s synchronous one (no real I/O in its own
/// implementation).
/// </summary>
public interface IPlanningClient
{
    Task<Plan> SubmitGoalAsync(Goal goal, CancellationToken cancellationToken = default);

    Task<GoalStatus> GetGoalStatusAsync(string goalId, CancellationToken cancellationToken = default);

    Task CancelGoalAsync(string goalId, string reason, CancellationToken cancellationToken = default);
}
