namespace EOS.Contracts;

/// <summary>
/// EOS-Specification.md Part 6 §6.1's Task Lifecycle states, reproduced verbatim
/// (Planning-Execution-Engine-Specification-v1.0 FR-PE7: "Every Task Lifecycle transition MUST
/// match Constitution Part 6 §6.2's existing transition table exactly"). WP-024's own components
/// (Scheduler, Execution Coordinator) only ever drive a <c>DispatchedTask</c> through
/// <see cref="Created"/> → <see cref="Planned"/> → <see cref="Ready"/> → <see cref="Running"/> —
/// every later state requires a real Task-executing role (Review/Testing/Verified/Released) or
/// the Retry/Rollback Manager (Waiting/Blocked/Retry, WP-025), neither of which exists yet.
/// </summary>
public enum TaskLifecycleState
{
    Created,
    Planned,
    Ready,
    Running,
    Waiting,
    Blocked,
    Retry,
    Review,
    Testing,
    Verified,
    Released,
    Archived,
    Cancelled,
}
