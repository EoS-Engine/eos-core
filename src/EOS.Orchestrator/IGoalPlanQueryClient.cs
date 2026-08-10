namespace EOS.Orchestrator;

/// <summary>
/// Composition Root Adapter Pattern (ADR-015-001): <c>EOS.Orchestrator</c> may not reference
/// <c>EOS.Planner</c> directly (Constitution Part 1 §1.2 — its only allowed outbound references
/// are <c>EOS.Contracts</c>/<c>EOS.Application</c>), so it declares this small interface for the
/// one read the Scheduler needs — the Goal's current <c>PlanId</c> pointer (WP-025 Architecture
/// Board Ruling Q1: "Use Goal.PlanId as the authoritative current-Plan pointer"), mirroring
/// <see cref="IPlanQueryClient"/>'s exact existing shape and documentation convention.
/// <c>EOS.Runner</c>'s composition root supplies the concrete adapter, backed by
/// <c>EOS.Planner</c>'s own already-built <c>GoalStore.GetByIdAsync</c> — no new persistence, no
/// duplicate store (ADR-PE002/FR-PE10).
/// </summary>
public interface IGoalPlanQueryClient
{
    Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default);
}
