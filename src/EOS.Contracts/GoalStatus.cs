namespace EOS.Contracts;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §21.1's <c>get_goal_status</c> read-only
/// Progress Tracking (§18.2) query.
/// </summary>
public sealed record GoalStatus(Guid GoalId, GoalLifecycleState State, Guid? PlanId);
