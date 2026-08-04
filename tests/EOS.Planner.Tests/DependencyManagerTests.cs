using EOS.Contracts;

namespace EOS.Planner.Tests;

public class DependencyManagerTests
{
    private static async Task<(DependencyManager Manager, GoalStore GoalStore)> CreateManagerAsync()
    {
        var goalDependencyStore = new GoalDependencyStore(TestConnectionString.SqlServer);
        await goalDependencyStore.EnsureTableExistsAsync(CancellationToken.None);
        var goalStore = new GoalStore(TestConnectionString.SqlServer);
        await goalStore.EnsureTableExistsAsync(CancellationToken.None);

        return (new DependencyManager(goalDependencyStore, goalStore), goalStore);
    }

    private static Goal NewGoal(GoalLifecycleState state = GoalLifecycleState.Proposed) => new(
        GoalId: Guid.NewGuid(),
        Statement: "Test goal",
        ParentGoalId: null,
        DomainTags: [],
        SubmittedByActor: "Product Owner",
        State: state,
        PlanId: null);

    [Fact]
    public async Task GetDependentGoalCountAsync_CountsEveryGoalThatDependsOnTheGivenGoal()
    {
        var (manager, _) = await CreateManagerAsync();
        var dependedUpon = Guid.NewGuid();

        await manager.AddGoalDependencyAsync(Guid.NewGuid(), dependedUpon, CancellationToken.None);
        await manager.AddGoalDependencyAsync(Guid.NewGuid(), dependedUpon, CancellationToken.None);

        var count = await manager.GetDependentGoalCountAsync(dependedUpon, CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task AddGoalDependencyAsync_IsIdempotent_ForTheSamePairAddedTwice()
    {
        var (manager, _) = await CreateManagerAsync();
        var goalId = Guid.NewGuid();
        var dependsOnGoalId = Guid.NewGuid();

        await manager.AddGoalDependencyAsync(goalId, dependsOnGoalId, CancellationToken.None);
        await manager.AddGoalDependencyAsync(goalId, dependsOnGoalId, CancellationToken.None);

        var count = await manager.GetDependentGoalCountAsync(dependsOnGoalId, CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task AreDependenciesSatisfiedAsync_ReturnsTrue_WhenTheGoalHasNoDependencies()
    {
        var (manager, _) = await CreateManagerAsync();

        var satisfied = await manager.AreDependenciesSatisfiedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(satisfied);
    }

    [Fact]
    public async Task AreDependenciesSatisfiedAsync_ReturnsFalse_WhenADependencyHasNotReachedCompleted()
    {
        var (manager, goalStore) = await CreateManagerAsync();
        var dependency = NewGoal(GoalLifecycleState.Planned);
        await goalStore.UpsertAsync(dependency, CancellationToken.None);
        var goalId = Guid.NewGuid();
        await manager.AddGoalDependencyAsync(goalId, dependency.GoalId, CancellationToken.None);

        var satisfied = await manager.AreDependenciesSatisfiedAsync(goalId, CancellationToken.None);

        Assert.False(satisfied);
    }

    [Fact]
    public async Task AreDependenciesSatisfiedAsync_ReturnsTrue_WhenEveryDependencyHasReachedCompleted()
    {
        var (manager, goalStore) = await CreateManagerAsync();
        var dependency = NewGoal(GoalLifecycleState.Completed);
        await goalStore.UpsertAsync(dependency, CancellationToken.None);
        var goalId = Guid.NewGuid();
        await manager.AddGoalDependencyAsync(goalId, dependency.GoalId, CancellationToken.None);

        var satisfied = await manager.AreDependenciesSatisfiedAsync(goalId, CancellationToken.None);

        Assert.True(satisfied);
    }
}
