namespace EOS.Planner;

/// <summary>
/// §11.5's three-part Goal Validation outcome, consumed by <see cref="PlanningEngine"/> — not a
/// public <c>IPlanningClient</c> contract type, since Goal Validation is an internal step of
/// <c>submit_goal()</c>, never a directly-queried result on its own.
/// </summary>
public sealed record GoalValidationResult(bool Feasible, string? Reason);
