using EOS.Contracts;

namespace EOS.Dashboard;

/// <summary>
/// EOS-Specification.md's own naming for this project ("Dashboard aggregation/query layer
/// (read-only, §0.11)") — combines the three WP-030-approved narrow <c>EOS.Contracts</c> read
/// interfaces into a single query surface. Depends only on <c>EOS.Contracts</c> (Constitution
/// §0.11/R-04); the concrete implementations of those interfaces are supplied by the
/// Composition Root (<c>EOS.Runner</c>), never referenced here. Goal lifecycle status is
/// deferred for WP-030 (no approved read/persistence path provides it) and is intentionally
/// absent from this surface.
/// </summary>
public sealed class DashboardQueryService(
    ILoopStatusQueryClient loopStatusQueryClient,
    ITaskStatusQueryClient taskStatusQueryClient,
    IRecentEventsQueryClient recentEventsQueryClient)
{
    public Task<LoopStatus> GetLoopStatusAsync(CancellationToken cancellationToken = default) =>
        loopStatusQueryClient.GetCurrentStatusAsync(cancellationToken);

    public Task<IReadOnlyList<DispatchedTask>> GetTasksByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default) =>
        taskStatusQueryClient.GetByStateAsync(state, cancellationToken);

    public Task<int> CountTasksByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default) =>
        taskStatusQueryClient.CountByStateAsync(state, cancellationToken);

    public Task<IReadOnlyList<RecentEventSummary>> GetRecentEventsAsync(int count, CancellationToken cancellationToken = default) =>
        recentEventsQueryClient.GetRecentAsync(count, cancellationToken);
}
