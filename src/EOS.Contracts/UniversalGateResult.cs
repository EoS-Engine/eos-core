namespace EOS.Contracts;

/// <summary>Outcome of one Universal Gate step (Constitution §0.8.1), ADR-009.</summary>
public enum GateStepStatus
{
    /// <summary>The step ran and passed.</summary>
    Passed,

    /// <summary>The step ran and failed, or could not run (toolchain missing, timeout) — fail closed.</summary>
    Failed,

    /// <summary>The step had nothing to verify (e.g. no test project owns any changed path) and was not run.</summary>
    NotApplicable,
}

/// <summary>One gate step's machine-checkable result with bounded diagnostic detail.</summary>
public sealed record GateStepResult(GateStepStatus Status, string? Detail);

/// <summary>
/// ADR-009: the raw, machine-checkable outcome of executing Constitution §0.8.1 Universal Gate 1
/// (static analysis/build) and Gate 2 (unit tests) against an isolated copy of the workspace with
/// the candidate artifact applied. This is measurement only — the pass/fail <em>decision</em>
/// belongs to <c>EOS.Gates</c>'s Rule Engine (Protection-Layer-Specification-v1.0 §10.3) and is
/// carried by <see cref="UniversalGateDecision"/>. Gate 2 is "unit test pass" only: the
/// Constitution's coverage threshold has no specified value and is deliberately not invented.
/// </summary>
public sealed record UniversalGateResult(GateStepResult BuildGate, GateStepResult TestGate);

/// <summary>
/// The Rule Engine's decision over a <see cref="UniversalGateResult"/>: <see cref="Passed"/> only
/// when Gate 1 passed and Gate 2 passed or was not applicable; otherwise <see cref="FailureReason"/>
/// names the failing gate with its bounded detail.
/// </summary>
public sealed record UniversalGateDecision(bool Passed, string? FailureReason, UniversalGateResult Result);
