using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Observable result of one <see cref="ExecutionCoordinator.ExecuteAndCompleteAsync"/> call
/// (Post-Roadmap WP-A), mirroring <see cref="DispatchOutcome"/>/<see cref="DispatchResult"/>:
/// lets callers and tests distinguish a real completion from a Protection denial of the
/// <c>TaskCompletion</c> transition and from an executing role's failure — every non-success
/// outcome having already been persisted as Constitution Part 6 §6.2's <c>Running → Blocked</c>.
/// </summary>
public enum ExecutionOutcome
{
    Completed,
    ProtectionDenied,
    ExecutionFailed,
}

/// <summary>
/// <see cref="Task"/> is the Task as persisted after the call (<c>Review</c> or <c>Blocked</c>);
/// <see cref="EvidenceRefs"/> is non-empty only for <see cref="ExecutionOutcome.Completed"/> and
/// <see cref="ExecutionOutcome.ProtectionDenied"/> (where the artifact was registered before the
/// denial and remains immutable); <see cref="Error"/> carries the failure text for
/// <see cref="ExecutionOutcome.ExecutionFailed"/> so the failure is observable to a caller that
/// has no logger of its own.
/// </summary>
public sealed record ExecutionResult(ExecutionOutcome Outcome, DispatchedTask Task, string[] EvidenceRefs, string? Error);
