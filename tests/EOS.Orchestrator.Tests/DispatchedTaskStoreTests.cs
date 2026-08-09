using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

public class DispatchedTaskStoreTests
{
    private static async Task<DispatchedTaskStore> CreateStoreAsync()
    {
        var store = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static DispatchedTask NewTask(
        TaskLifecycleState state = TaskLifecycleState.Created,
        int priority = 0,
        Guid[]? dependsOnTaskIds = null,
        SchedulingMode schedulingMode = SchedulingMode.Immediate,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? runningAt = null,
        bool eventObserved = false) => new(
        TaskId: Guid.NewGuid(),
        PlanId: Guid.NewGuid(),
        GoalId: Guid.NewGuid(),
        Description: "Test task",
        CompetencyRequirements: ["logging"],
        DependsOnTaskIds: dependsOnTaskIds ?? [],
        Priority: priority,
        State: state,
        SchedulingMode: schedulingMode,
        NotBefore: notBefore,
        RunningAt: runningAt,
        EventObserved: eventObserved);

    [Fact]
    public async Task UpsertAsync_ThenGetByIdAsync_RoundTripsEveryField()
    {
        var store = await CreateStoreAsync();
        var dependsOn = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var task = NewTask(state: TaskLifecycleState.Planned, priority: 7, dependsOnTaskIds: dependsOn, schedulingMode: SchedulingMode.Delayed, notBefore: DateTimeOffset.UtcNow.AddHours(1), eventObserved: true);

        await store.UpsertAsync(task, CancellationToken.None);
        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);

        // Field-by-field, not Assert.Equal(task, persisted) — DispatchedTask's record-generated
        // Equals compares its string[]/Guid[] fields by reference, not content, so a freshly
        // JSON-deserialized array (necessarily a different instance) would never compare equal
        // even when its contents genuinely match.
        Assert.NotNull(persisted);
        Assert.Equal(task.TaskId, persisted.TaskId);
        Assert.Equal(task.PlanId, persisted.PlanId);
        Assert.Equal(task.GoalId, persisted.GoalId);
        Assert.Equal(task.Description, persisted.Description);
        Assert.Equal(task.CompetencyRequirements, persisted.CompetencyRequirements);
        Assert.Equal(task.DependsOnTaskIds, persisted.DependsOnTaskIds);
        Assert.Equal(task.Priority, persisted.Priority);
        Assert.Equal(task.State, persisted.State);
        Assert.Equal(task.SchedulingMode, persisted.SchedulingMode);
        Assert.Equal(task.NotBefore, persisted.NotBefore);
        Assert.Equal(task.RunningAt, persisted.RunningAt);
        Assert.Equal(task.EventObserved, persisted.EventObserved);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesAnExistingRow_WhenCalledTwiceForTheSameTaskId()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(state: TaskLifecycleState.Created);

        await store.UpsertAsync(task, CancellationToken.None);
        var updated = task with { State = TaskLifecycleState.Ready };
        await store.UpsertAsync(updated, CancellationToken.None);

        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persisted!.State);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_ForANonExistentTask()
    {
        var store = await CreateStoreAsync();

        var persisted = await store.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(persisted);
    }

    [Fact]
    public async Task GetByStateAsync_ReturnsOnlyTasksInTheGivenState()
    {
        var store = await CreateStoreAsync();
        var planned = NewTask(state: TaskLifecycleState.Planned);
        var ready = NewTask(state: TaskLifecycleState.Ready);
        await store.UpsertAsync(planned, CancellationToken.None);
        await store.UpsertAsync(ready, CancellationToken.None);

        var plannedTasks = await store.GetByStateAsync(TaskLifecycleState.Planned, CancellationToken.None);

        Assert.Contains(plannedTasks, task => task.TaskId == planned.TaskId);
        Assert.DoesNotContain(plannedTasks, task => task.TaskId == ready.TaskId);
    }

    [Fact]
    public async Task GetReadyTasksOrderedByPriorityAsync_OrdersHighestPriorityFirst()
    {
        var store = await CreateStoreAsync();
        var low = NewTask(state: TaskLifecycleState.Ready, priority: 1);
        var high = NewTask(state: TaskLifecycleState.Ready, priority: 9);
        await store.UpsertAsync(low, CancellationToken.None);
        await store.UpsertAsync(high, CancellationToken.None);

        var ready = await store.GetReadyTasksOrderedByPriorityAsync(CancellationToken.None);

        var highIndex = ready.ToList().FindIndex(task => task.TaskId == high.TaskId);
        var lowIndex = ready.ToList().FindIndex(task => task.TaskId == low.TaskId);
        Assert.True(highIndex < lowIndex);
    }

    // CodeRabbit PR #21 round 1: a lower-bound-only assertion (">= 2") would still pass even if
    // CountByStateAsync ignored the State filter entirely, since the shared real SQL Server table
    // already has rows from other tests. Capturing the baseline before inserting and asserting
    // the exact delta actually proves the SQL predicate filters correctly.
    [Fact]
    public async Task CountByStateAsync_CountsOnlyTasksInTheGivenState()
    {
        var store = await CreateStoreAsync();
        var runningBaseline = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        var readyBaseline = await store.CountByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);

        await store.UpsertAsync(NewTask(state: TaskLifecycleState.Running), CancellationToken.None);
        await store.UpsertAsync(NewTask(state: TaskLifecycleState.Running), CancellationToken.None);
        await store.UpsertAsync(NewTask(state: TaskLifecycleState.Ready), CancellationToken.None);

        var runningCountAfter = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        var readyCountAfter = await store.CountByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);

        Assert.Equal(runningBaseline + 2, runningCountAfter);
        Assert.Equal(readyBaseline + 1, readyCountAfter);
    }

    // CodeRabbit PR #21 round 1: same reasoning — a lower-bound-only assertion would still pass
    // even if CountRunningSinceAsync ignored the "since" cutoff entirely. Capturing the baseline
    // at the same cutoff before inserting proves only the post-cutoff task is counted.
    [Fact]
    public async Task CountRunningSinceAsync_CountsOnlyTasksWithRunningAtOnOrAfterTheGivenTime()
    {
        var store = await CreateStoreAsync();
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var baseline = await store.CountRunningSinceAsync(since, CancellationToken.None);

        var recent = NewTask(state: TaskLifecycleState.Running, runningAt: DateTimeOffset.UtcNow);
        var old = NewTask(state: TaskLifecycleState.Running, runningAt: DateTimeOffset.UtcNow.AddDays(-2));
        await store.UpsertAsync(recent, CancellationToken.None);
        await store.UpsertAsync(old, CancellationToken.None);

        var countAfter = await store.CountRunningSinceAsync(since, CancellationToken.None);

        Assert.Equal(baseline + 1, countAfter);
    }
}
