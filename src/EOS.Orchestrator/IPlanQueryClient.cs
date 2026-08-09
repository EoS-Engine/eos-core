using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Composition Root Adapter Pattern (ADR-015-001): <c>EOS.Orchestrator</c> may not reference
/// <c>EOS.Planner</c> directly (Constitution Part 1 §1.2 — its only allowed outbound references
/// are <c>EOS.Contracts</c>/<c>EOS.Application</c>), so it declares this small interface for the
/// one read it needs — the full <see cref="Plan"/> (including every <see cref="PlanTask"/>'s
/// <c>DependsOnTaskIds</c>, needed to materialize the Dependency Graph, Constitution Part 7
/// §7.2) once <c>PlannerGenerated</c> (§20) announces it exists. <c>EOS.Runner</c>'s composition
/// root supplies the concrete adapter, backed by <c>EOS.Planner</c>'s own already-built
/// <c>PlanStore.GetByIdAsync</c> — no new persistence, no duplicate store (ADR-PE002/FR-PE10).
/// </summary>
public interface IPlanQueryClient
{
    Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default);
}
