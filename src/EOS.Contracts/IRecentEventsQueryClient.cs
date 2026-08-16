namespace EOS.Contracts;

public interface IRecentEventsQueryClient
{
    Task<IReadOnlyList<RecentEventSummary>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken = default);
}
