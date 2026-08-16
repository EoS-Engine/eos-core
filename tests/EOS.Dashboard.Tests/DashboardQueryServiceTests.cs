using EOS.Contracts;

namespace EOS.Dashboard.Tests;

public class DashboardQueryServiceTests
{
    [Fact]
    public async Task GetLoopStatusAsync_DelegatesToLoopStatusQueryClient()
    {
        var expected = new LoopStatus(Guid.NewGuid(), OperationalMode.Assisted, 0.75);
        var service = new DashboardQueryService(
            new FixedLoopStatusQueryClient(expected),
            new FixedTaskStatusQueryClient([], 0),
            new FixedRecentEventsQueryClient([]));

        var actual = await service.GetLoopStatusAsync();

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task GetTasksByStateAsync_DelegatesRequestedStateAndReturnsResult()
    {
        var expected = new DispatchedTask[]
        {
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Do the thing", [], [], 1,
                TaskLifecycleState.Ready, SchedulingMode.Immediate, null, null, false, 0, null),
        };
        var client = new FixedTaskStatusQueryClient(expected, 0);
        var service = new DashboardQueryService(
            new FixedLoopStatusQueryClient(new LoopStatus(null, OperationalMode.Assisted, null)),
            client,
            new FixedRecentEventsQueryClient([]));

        var actual = await service.GetTasksByStateAsync(TaskLifecycleState.Ready);

        Assert.Same(expected, actual);
        Assert.Equal(TaskLifecycleState.Ready, client.LastRequestedState);
    }

    [Fact]
    public async Task CountTasksByStateAsync_DelegatesRequestedStateAndReturnsResult()
    {
        var client = new FixedTaskStatusQueryClient([], 7);
        var service = new DashboardQueryService(
            new FixedLoopStatusQueryClient(new LoopStatus(null, OperationalMode.Assisted, null)),
            client,
            new FixedRecentEventsQueryClient([]));

        var actual = await service.CountTasksByStateAsync(TaskLifecycleState.Blocked);

        Assert.Equal(7, actual);
        Assert.Equal(TaskLifecycleState.Blocked, client.LastRequestedState);
    }

    [Fact]
    public async Task GetRecentEventsAsync_DelegatesRequestedCountAndReturnsResult()
    {
        var expected = new RecentEventSummary[]
        {
            new(Guid.NewGuid(), "GoalCreated", "EOS.Planner", DateTimeOffset.UtcNow, "{}"),
        };
        var client = new FixedRecentEventsQueryClient(expected);
        var service = new DashboardQueryService(
            new FixedLoopStatusQueryClient(new LoopStatus(null, OperationalMode.Assisted, null)),
            new FixedTaskStatusQueryClient([], 0),
            client);

        var actual = await service.GetRecentEventsAsync(10);

        Assert.Same(expected, actual);
        Assert.Equal(10, client.LastRequestedCount);
    }

    private sealed class FixedLoopStatusQueryClient(LoopStatus status) : ILoopStatusQueryClient
    {
        public Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);
    }

    private sealed class FixedTaskStatusQueryClient(IReadOnlyList<DispatchedTask> tasks, int count) : ITaskStatusQueryClient
    {
        public TaskLifecycleState? LastRequestedState { get; private set; }

        public Task<IReadOnlyList<DispatchedTask>> GetByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default)
        {
            LastRequestedState = state;
            return Task.FromResult(tasks);
        }

        public Task<int> CountByStateAsync(TaskLifecycleState state, CancellationToken cancellationToken = default)
        {
            LastRequestedState = state;
            return Task.FromResult(count);
        }
    }

    private sealed class FixedRecentEventsQueryClient(IReadOnlyList<RecentEventSummary> events) : IRecentEventsQueryClient
    {
        public int? LastRequestedCount { get; private set; }

        public Task<IReadOnlyList<RecentEventSummary>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        {
            LastRequestedCount = count;
            return Task.FromResult(events);
        }
    }
}
