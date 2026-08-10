using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// A computed, non-persisted snapshot (never a stored counter, ADR-PE002/FR-PE10) of a Goal's
/// current-Plan-filtered Task states — Planning-Execution-Engine-Specification-v1.0 §18.2's Goal
/// Status, aggregated per §11.7's completion rule. <see cref="TotalTaskCount"/> and
/// <see cref="CountByState"/> reflect only <see cref="DispatchedTask"/> rows whose
/// <c>PlanId</c> equals the Goal's current <c>PlanId</c> (WP-025 Architecture Board Ruling
/// Q1/Q2) — superseded-Plan rows contribute nothing here, though they remain persisted unchanged.
/// </summary>
public sealed record GoalProgress(
    Guid GoalId,
    int TotalTaskCount,
    IReadOnlyDictionary<TaskLifecycleState, int> CountByState,
    double PercentComplete);
