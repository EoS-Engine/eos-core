using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

/// <summary>
/// WP-025.4: WP-025 Architecture Board Ruling Q1/Q2 (frozen) — a Task is dispatchable only when
/// <c>DispatchedTask.PlanId == Goal.PlanId</c>. Old-Plan rows are never cancelled, mutated, or
/// deleted; their non-dispatchability comes solely from this pointer comparison.
/// </summary>
public class SchedulerCurrentPlanFilteringTests
{
    private static async Task<DispatchedTaskStore> CreateStoreAsync()
    {
        var store = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static async Task ClearReadyQueueAsync(DispatchedTaskStore store)
    {
        var ready = await store.GetByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);
        foreach (var task in ready)
        {
            await store.UpsertAsync(task with { State = TaskLifecycleState.Cancelled }, CancellationToken.None);
        }
    }

    private static DispatchedTask NewTask(Guid goalId, Guid planId, TaskLifecycleState state, int priority = 1) => new(
        TaskId: Guid.NewGuid(),
        PlanId: planId,
        GoalId: goalId,
        Description: "Current-Plan filtering test task",
        CompetencyRequirements: ["logging"],
        DependsOnTaskIds: [],
        Priority: priority,
        State: state,
        SchedulingMode: SchedulingMode.Immediate,
        NotBefore: null,
        RunningAt: null,
        EventObserved: false,
        RetryCount: 0,
        BlockedReason: null);

    private static Scheduler NewScheduler(DispatchedTaskStore store, IGoalPlanQueryClient goalPlanQueryClient) =>
        new(store, new FixedPlanQueryClient(), goalPlanQueryClient, new FixedTierResourceManagementClient(CapacityTier.Safe),
            concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

    // ---------- Test 1 — Current Plan Task ----------

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_SelectsAReadyTask_WhenItsPlanIdMatchesTheGoalsCurrentPlanId()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var task = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        var scheduler = NewScheduler(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.NotNull(selected);
        Assert.Equal(task.TaskId, selected.TaskId);
    }

    // ---------- Test 2 — Old Plan Ready Task ----------

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_RejectsAReadyTask_WhenItsPlanIdIsNotTheGoalsCurrentPlanId()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var oldPlanId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var oldPlanTask = NewTask(goalId, oldPlanId, TaskLifecycleState.Ready);
        await store.UpsertAsync(oldPlanTask, CancellationToken.None);
        var scheduler = NewScheduler(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selected);
    }

    // ---------- Test 3 — Old Plan Planned Task ----------

    [Fact]
    public async Task EvaluateReadinessAsync_DoesNotMakeAnOldPlanPlannedTaskDispatchable_AfterItTransitionsToReady()
    {
        // CodeRabbit PR #22 round 1: corrected — the current-Plan filter gates BOTH
        // EvaluateReadinessAsync and SelectNextDispatchableTaskAsync (Scheduler.cs's own
        // IsCurrentPlanAsync is applied in both methods). An old-Plan Planned task is therefore
        // left exactly as-is — never promoted to Ready, never selected for dispatch.
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var oldPlanId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var oldPlanTask = NewTask(goalId, oldPlanId, TaskLifecycleState.Planned);
        await store.UpsertAsync(oldPlanTask, CancellationToken.None);
        var scheduler = NewScheduler(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selected);
        var persisted = await store.GetByIdAsync(oldPlanTask.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Planned, persisted!.State);
    }

    // ---------- Test 4 — Goal Plan lookup ----------

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_ObtainsTheCurrentPlanId_ThroughIGoalPlanQueryClient()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var task = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        var recordingGoalPlanQueryClient = new RecordingGoalPlanQueryClient(currentPlanId);
        var scheduler = NewScheduler(store, recordingGoalPlanQueryClient);

        await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Contains(goalId, recordingGoalPlanQueryClient.QueriedGoalIds);
    }

    // ---------- Test 5 — No mutation ----------

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_LeavesARejectedOldPlanTask_CompletelyUnchanged()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var oldPlanId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var oldPlanTask = NewTask(goalId, oldPlanId, TaskLifecycleState.Ready);
        await store.UpsertAsync(oldPlanTask, CancellationToken.None);
        var scheduler = NewScheduler(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        var persisted = await store.GetByIdAsync(oldPlanTask.TaskId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(oldPlanTask.State, persisted.State);
        Assert.Equal(oldPlanTask.PlanId, persisted.PlanId);
        Assert.Equal(oldPlanTask.TaskId, persisted.TaskId);
        Assert.Equal(oldPlanTask.GoalId, persisted.GoalId);
    }

    // ---------- Test 6 — Existing Scheduler gates preserved ----------

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_StillAppliesConcurrencyCeiling_AlongsideTheCurrentPlanFilter()
    {
        var store = await CreateStoreAsync();
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var task = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        await store.UpsertAsync(
            NewTask(goalId, currentPlanId, TaskLifecycleState.Running) with { RunningAt = DateTimeOffset.UtcNow },
            CancellationToken.None);
        var scheduler = new Scheduler(
            store, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient((goalId, currentPlanId)),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1, dailyCapacity: 100);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        // Concurrency ceiling (already-Running count >= ceiling) still rejects, exactly as before
        // WP-025.4 — the current-Plan filter is additive, not a replacement for existing gates.
        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_StillAppliesPriorityOrdering_AlongsideTheCurrentPlanFilter()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var low = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready, priority: 1);
        var high = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready, priority: 9);
        await store.UpsertAsync(low, CancellationToken.None);
        await store.UpsertAsync(high, CancellationToken.None);
        var scheduler = NewScheduler(store, new FixedGoalPlanQueryClient((goalId, currentPlanId)));

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        // Priority Queue ordering (§7.3 step 1) unaffected by the additive current-Plan filter.
        Assert.Equal(high.TaskId, selected!.TaskId);
    }

    // ---------- Test 7 — Query failure ----------

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_Propagates_WhenIGoalPlanQueryClientThrows()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var task = NewTask(goalId, Guid.NewGuid(), TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        var scheduler = NewScheduler(store, new ThrowingGoalPlanQueryClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None));

        // Not silently dispatched, no fabricated fallback PlanId.
        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persisted!.State);
    }

    // ---------- Test 8 — Cancellation ----------

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_PropagatesCancellation_WhenTheGoalPlanLookupIsCancelled()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var task = NewTask(goalId, Guid.NewGuid(), TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        var scheduler = NewScheduler(store, new CancellationCheckingGoalPlanQueryClient());
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scheduler.SelectNextDispatchableTaskAsync(alreadyCancelled.Token));
    }

    // ---------- Critical Regression Scenario (§7) ----------

    [Fact]
    public async Task CriticalRegressionScenario_OnlyCurrentPlanTasksAreDispatchable_OldPlanTasksRemainUnchanged()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var planA = Guid.NewGuid();
        var planB = Guid.NewGuid();

        var taskA1 = NewTask(goalId, planA, TaskLifecycleState.Ready);
        var taskA2 = NewTask(goalId, planA, TaskLifecycleState.Planned);
        var taskB1 = NewTask(goalId, planB, TaskLifecycleState.Ready);
        var taskB2 = NewTask(goalId, planB, TaskLifecycleState.Planned);
        await store.UpsertAsync(taskA1, CancellationToken.None);
        await store.UpsertAsync(taskA2, CancellationToken.None);
        await store.UpsertAsync(taskB1, CancellationToken.None);
        await store.UpsertAsync(taskB2, CancellationToken.None);

        // Goal's current Plan is Plan-B.
        var scheduler = NewScheduler(store, new FixedGoalPlanQueryClient((goalId, planB)));

        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        // Task-B1 and Task-B2 (current Plan) are both eligible according to existing Scheduler
        // rules — EvaluateReadinessAsync already promoted Task-B2 to Ready (no dependencies), so
        // both are equal-priority Ready candidates; which one SelectNextDispatchableTaskAsync
        // picks first is the store's own pre-existing, disclosed no-tiebreaker-on-ties behavior
        // (unrelated to WP-025.4) — what matters here is that the selection is never Task-A1.
        Assert.NotNull(selected);
        Assert.Contains(selected.TaskId, new[] { taskB1.TaskId, taskB2.TaskId });
        var persistedB2 = await store.GetByIdAsync(taskB2.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persistedB2!.State);

        // Task-A1/Task-A2 (old Plan-A) remain persisted exactly as they were — never selected,
        // never cancelled, never mutated.
        var persistedA1 = await store.GetByIdAsync(taskA1.TaskId, CancellationToken.None);
        var persistedA2 = await store.GetByIdAsync(taskA2.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persistedA1!.State);
        Assert.Equal(planA, persistedA1.PlanId);
        Assert.Equal(TaskLifecycleState.Planned, persistedA2!.State);
        Assert.Equal(planA, persistedA2.PlanId);
    }

    private sealed class RecordingGoalPlanQueryClient(Guid currentPlanId) : IGoalPlanQueryClient
    {
        public List<Guid> QueriedGoalIds { get; } = [];

        public Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default)
        {
            QueriedGoalIds.Add(goalId);
            return Task.FromResult<Guid?>(currentPlanId);
        }
    }

    private sealed class ThrowingGoalPlanQueryClient : IGoalPlanQueryClient
    {
        public Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated Goal/Plan lookup infrastructure failure.");
    }

    private sealed class CancellationCheckingGoalPlanQueryClient : IGoalPlanQueryClient
    {
        public Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }
    }
}
