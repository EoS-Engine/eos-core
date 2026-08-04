namespace EOS.Contracts;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §11.1's Goal Lifecycle:
/// <c>Proposed → Validated → Decomposing → Planned → Executing → (Paused|Blocked) → Completed</c>,
/// with <c>Cancelled</c> reachable from any state (§11.6). <c>Executing</c>/<c>Paused</c>/
/// <c>Blocked</c> are populated only once Execution Coordinator (§10.7, WP-024) exists; WP-023's
/// own components only ever produce <c>Proposed</c>, <c>Validated</c>, <c>Decomposing</c>,
/// <c>Planned</c>, and <c>Cancelled</c>.
/// </summary>
public enum GoalLifecycleState
{
    Proposed,
    Validated,
    Decomposing,
    Planned,
    Executing,
    Paused,
    Blocked,
    Completed,
    Cancelled,
}
