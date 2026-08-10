using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Composition Root Adapter Pattern (ADR-015-001): <c>EOS.Orchestrator</c> may not reference
/// <c>EOS.Planner</c> directly (Constitution Part 1 §1.2 — its only allowed outbound references
/// are <c>EOS.Contracts</c>/<c>EOS.Application</c>), so it declares this small interface for the
/// one call it needs — Planning-Execution-Engine-Specification-v1.0 §16.1's failure-triggered
/// Dynamic Replanning (a Task permanently <see cref="TaskLifecycleState.Blocked"/>, §13.7),
/// mirroring <see cref="IPlanQueryClient"/>/<see cref="IGoalPlanQueryClient"/>'s exact existing
/// shape and documentation convention. <c>EOS.Runner</c>'s composition root supplies the concrete
/// adapter, backed by <c>EOS.Planner</c>'s own already-built
/// <c>PlanningEngine.ReplanAfterFailureAsync</c> — no new persistence, no duplicate Planning
/// Engine logic (ADR-PE001).
/// </summary>
public interface IReplanRequestClient
{
    Task<Plan> RequestReplanAfterFailureAsync(Guid goalId, CancellationToken cancellationToken = default);
}
