namespace EOS.Contracts;

/// <summary>
/// WP-030 (Dashboard) — narrow, read-only projection over dispatched Task state, adapting
/// <c>EOS.Orchestrator.DispatchedTaskStore</c>'s existing <c>GetByStateAsync</c>/<c>CountByStateAsync</c>
/// query methods (unchanged) without exposing that store's own write method (<c>UpsertAsync</c>)
/// to Dashboard, matching this codebase's own Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface ITaskStatusQueryClient
{
    Task<IReadOnlyList<DispatchedTask>> GetByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default);

    Task<int> CountByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default);
}
