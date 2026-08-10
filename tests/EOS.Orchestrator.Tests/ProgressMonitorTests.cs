using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

/// <summary>
/// WP-025.5: Planning-Execution-Engine-Specification-v1.0 §18's Progress Monitor — computed,
/// non-persisted, current-Plan-filtered (WP-025 Architecture Board Ruling Q1/Q2) aggregation.
/// </summary>
public class ProgressMonitorTests
{
    private static async Task<DispatchedTaskStore> CreateStoreAsync()
    {
        var store = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static DispatchedTask NewTask(Guid goalId, Guid planId, TaskLifecycleState state) => new(
        TaskId: Guid.NewGuid(),
        PlanId: planId,
        GoalId: goalId,
        Description: "Progress test task",
        CompetencyRequirements: ["logging"],
        DependsOnTaskIds: [],
        Priority: 1,
        State: state,
        SchedulingMode: SchedulingMode.Immediate,
        NotBefore: null,
        RunningAt: null,
        EventObserved: false,
        RetryCount: 0,
        BlockedReason: null);

    // ---------- 1. Task progress ----------

    [Fact]
    public async Task GetTaskProgressAsync_ReflectsTheTasksCurrentLifecycleState()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(Guid.NewGuid(), Guid.NewGuid(), TaskLifecycleState.Testing);
        await store.UpsertAsync(task, CancellationToken.None);
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient());

        var progress = await progressMonitor.GetTaskProgressAsync(task.TaskId, CancellationToken.None);

        // No invented vocabulary: the return value is exactly TaskLifecycleState, not a wrapper.
        Assert.Equal(TaskLifecycleState.Testing, progress);
    }

    [Fact]
    public async Task GetTaskProgressAsync_ReturnsNull_ForANonExistentTask()
    {
        var store = await CreateStoreAsync();
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient());

        var progress = await progressMonitor.GetTaskProgressAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(progress);
    }

    // ---------- 2. Goal progress ----------

    [Fact]
    public async Task GetGoalProgressAsync_CountsOnlyCurrentPlanTasks_WithCorrectPerStateCountsAndPercentage()
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var oldPlanId = Guid.NewGuid();

        // Current Plan: 1 Released (complete), 1 Running, 2 Ready.
        var released = NewTask(goalId, currentPlanId, TaskLifecycleState.Released);
        var running = NewTask(goalId, currentPlanId, TaskLifecycleState.Running);
        var ready1 = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready);
        var ready2 = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready);
        // Old Plan: must not contribute.
        var oldPlanTask = NewTask(goalId, oldPlanId, TaskLifecycleState.Released);
        await store.UpsertAsync(released, CancellationToken.None);
        await store.UpsertAsync(running, CancellationToken.None);
        await store.UpsertAsync(ready1, CancellationToken.None);
        await store.UpsertAsync(ready2, CancellationToken.None);
        await store.UpsertAsync(oldPlanTask, CancellationToken.None);
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var progress = await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);

        Assert.Equal(goalId, progress.GoalId);
        Assert.Equal(4, progress.TotalTaskCount);
        Assert.Equal(1, progress.CountByState[TaskLifecycleState.Released]);
        Assert.Equal(1, progress.CountByState[TaskLifecycleState.Running]);
        Assert.Equal(2, progress.CountByState[TaskLifecycleState.Ready]);
        Assert.False(progress.CountByState.ContainsKey(TaskLifecycleState.Blocked));
        // 1 of 4 current-Plan Tasks Released (PE §11.7's completion definition) = 25%.
        Assert.Equal(25.0, progress.PercentComplete);
    }

    // ---------- 3. Critical old-Plan regression ----------

    [Fact]
    public async Task GetGoalProgressAsync_CountsOnlyTheNewCurrentPlanTasks_WhenAnOldPlanAndANewPlanBothExist()
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var oldPlanId = Guid.NewGuid();
        var newPlanId = Guid.NewGuid();

        // Old Plan: a full spread of states — must contribute nothing.
        var oldPlanned = NewTask(goalId, oldPlanId, TaskLifecycleState.Planned);
        var oldReady = NewTask(goalId, oldPlanId, TaskLifecycleState.Ready);
        var oldRunning = NewTask(goalId, oldPlanId, TaskLifecycleState.Running);
        await store.UpsertAsync(oldPlanned, CancellationToken.None);
        await store.UpsertAsync(oldReady, CancellationToken.None);
        await store.UpsertAsync(oldRunning, CancellationToken.None);

        // New Plan: various states.
        var newPlanned = NewTask(goalId, newPlanId, TaskLifecycleState.Planned);
        var newReleased = NewTask(goalId, newPlanId, TaskLifecycleState.Released);
        await store.UpsertAsync(newPlanned, CancellationToken.None);
        await store.UpsertAsync(newReleased, CancellationToken.None);

        // Goal's current Plan is the new one.
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, newPlanId)));

        var progress = await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);

        Assert.Equal(2, progress.TotalTaskCount);
        Assert.Equal(1, progress.CountByState[TaskLifecycleState.Planned]);
        Assert.Equal(1, progress.CountByState[TaskLifecycleState.Released]);
        Assert.False(progress.CountByState.ContainsKey(TaskLifecycleState.Ready));
        Assert.False(progress.CountByState.ContainsKey(TaskLifecycleState.Running));
        Assert.Equal(50.0, progress.PercentComplete);

        // Old-Plan rows remain persisted, exactly as they were — never touched by progress
        // computation (a read-only observer, §18).
        var persistedOldPlanned = await store.GetByIdAsync(oldPlanned.TaskId, CancellationToken.None);
        var persistedOldReady = await store.GetByIdAsync(oldReady.TaskId, CancellationToken.None);
        var persistedOldRunning = await store.GetByIdAsync(oldRunning.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Planned, persistedOldPlanned!.State);
        Assert.Equal(oldPlanId, persistedOldPlanned.PlanId);
        Assert.Equal(TaskLifecycleState.Ready, persistedOldReady!.State);
        Assert.Equal(oldPlanId, persistedOldReady.PlanId);
        Assert.Equal(TaskLifecycleState.Running, persistedOldRunning!.State);
        Assert.Equal(oldPlanId, persistedOldRunning.PlanId);
    }

    // ---------- 4. Workflow progress ----------

    [Fact]
    public async Task GetWorkflowProgressAsync_ComputesPerGoalProgress_GroupedByGoalId_ForTheGivenGoals()
    {
        var store = await CreateStoreAsync();
        var goalOneId = Guid.NewGuid();
        var goalTwoId = Guid.NewGuid();
        var planOne = Guid.NewGuid();
        var planTwo = Guid.NewGuid();

        await store.UpsertAsync(NewTask(goalOneId, planOne, TaskLifecycleState.Released), CancellationToken.None);
        await store.UpsertAsync(NewTask(goalOneId, planOne, TaskLifecycleState.Running), CancellationToken.None);
        await store.UpsertAsync(NewTask(goalTwoId, planTwo, TaskLifecycleState.Blocked), CancellationToken.None);
        var goalPlanQueryClient = new FixedGoalPlanQueryClient((goalOneId, planOne), (goalTwoId, planTwo));
        var progressMonitor = new ProgressMonitor(store, goalPlanQueryClient);

        var workflowProgress = await progressMonitor.GetWorkflowProgressAsync([goalOneId, goalTwoId], CancellationToken.None);

        Assert.Equal(2, workflowProgress.Count);
        Assert.Equal(2, workflowProgress[goalOneId].TotalTaskCount);
        Assert.Equal(1, workflowProgress[goalOneId].CountByState[TaskLifecycleState.Released]);
        Assert.Equal(1, workflowProgress[goalTwoId].TotalTaskCount);
        Assert.Equal(1, workflowProgress[goalTwoId].CountByState[TaskLifecycleState.Blocked]);
    }

    // ---------- 5. Empty Goal / no current Tasks ----------

    [Fact]
    public async Task GetGoalProgressAsync_ReturnsZeroTotalAndZeroPercent_ForAGoalWithNoTasksAtAll()
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var progress = await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);

        Assert.Equal(0, progress.TotalTaskCount);
        Assert.Empty(progress.CountByState);
        // No divide-by-zero — 0.0, not NaN/Infinity/an invented status.
        Assert.Equal(0.0, progress.PercentComplete);
    }

    // ---------- 6. Goal with only old-Plan Tasks ----------

    [Fact]
    public async Task GetGoalProgressAsync_ReturnsZero_WhenOnlyOldPlanTasksExist_AndTheCurrentPlanHasNone()
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var oldPlanId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        await store.UpsertAsync(NewTask(goalId, oldPlanId, TaskLifecycleState.Released), CancellationToken.None);
        await store.UpsertAsync(NewTask(goalId, oldPlanId, TaskLifecycleState.Running), CancellationToken.None);
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var progress = await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);

        Assert.Equal(0, progress.TotalTaskCount);
        Assert.Empty(progress.CountByState);
        Assert.Equal(0.0, progress.PercentComplete);
    }

    // ---------- 7. Mixed lifecycle states ----------

    [Theory]
    [InlineData(TaskLifecycleState.Created)]
    [InlineData(TaskLifecycleState.Planned)]
    [InlineData(TaskLifecycleState.Ready)]
    [InlineData(TaskLifecycleState.Running)]
    [InlineData(TaskLifecycleState.Waiting)]
    [InlineData(TaskLifecycleState.Blocked)]
    [InlineData(TaskLifecycleState.Retry)]
    [InlineData(TaskLifecycleState.Review)]
    [InlineData(TaskLifecycleState.Testing)]
    [InlineData(TaskLifecycleState.Verified)]
    [InlineData(TaskLifecycleState.Released)]
    [InlineData(TaskLifecycleState.Archived)]
    [InlineData(TaskLifecycleState.Cancelled)]
    public async Task GetGoalProgressAsync_CorrectlyCounts_EveryStateDefinedInTaskLifecycleState(TaskLifecycleState state)
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var task = NewTask(goalId, currentPlanId, state);
        await store.UpsertAsync(task, CancellationToken.None);
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var progress = await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);

        Assert.Equal(1, progress.TotalTaskCount);
        Assert.Equal(1, progress.CountByState[state]);
    }

    // PE §11.7: "A Goal reaches Completed only when every leaf Task... has reached Released or
    // Archived" — the only two states this codebase's frozen documents name as "complete."
    [Theory]
    [InlineData(TaskLifecycleState.Released, 100.0)]
    [InlineData(TaskLifecycleState.Archived, 100.0)]
    [InlineData(TaskLifecycleState.Verified, 0.0)]
    [InlineData(TaskLifecycleState.Running, 0.0)]
    public async Task GetGoalProgressAsync_TreatsOnlyReleasedAndArchived_AsCompleteForThePercentageCalculation(
        TaskLifecycleState state, double expectedPercent)
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        await store.UpsertAsync(NewTask(goalId, currentPlanId, state), CancellationToken.None);
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var progress = await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);

        Assert.Equal(expectedPercent, progress.PercentComplete);
    }

    // ---------- 8. Current Plan lookup failure ----------

    [Fact]
    public async Task GetGoalProgressAsync_Propagates_WhenIGoalPlanQueryClientThrows()
    {
        var store = await CreateStoreAsync();
        var progressMonitor = new ProgressMonitor(store, new ThrowingGoalPlanQueryClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => progressMonitor.GetGoalProgressAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ---------- 9. Cancellation ----------

    [Fact]
    public async Task GetGoalProgressAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        await store.UpsertAsync(NewTask(goalId, currentPlanId, TaskLifecycleState.Ready), CancellationToken.None);
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => progressMonitor.GetGoalProgressAsync(goalId, alreadyCancelled.Token));
    }

    [Fact]
    public async Task GetTaskProgressAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        var store = await CreateStoreAsync();
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient());
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => progressMonitor.GetTaskProgressAsync(Guid.NewGuid(), alreadyCancelled.Token));
    }

    // ---------- 10. No persistence ----------

    [Fact]
    public async Task GetGoalProgressAsync_PerformsNoWrites_TaskRowsAreByteForByteUnchangedAfterward()
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var task = NewTask(goalId, currentPlanId, TaskLifecycleState.Running);
        await store.UpsertAsync(task, CancellationToken.None);
        var progressMonitor = new ProgressMonitor(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);
        await progressMonitor.GetGoalProgressAsync(goalId, CancellationToken.None);
        await progressMonitor.GetTaskProgressAsync(task.TaskId, CancellationToken.None);

        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(task.State, persisted!.State);
        Assert.Equal(task.PlanId, persisted.PlanId);
        Assert.Equal(task.RetryCount, persisted.RetryCount);
        Assert.Equal(task.BlockedReason, persisted.BlockedReason);
    }

    private sealed class ThrowingGoalPlanQueryClient : IGoalPlanQueryClient
    {
        public Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated Goal/Plan lookup infrastructure failure.");
    }
}
