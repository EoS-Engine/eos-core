using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

public class ExecutionCoordinatorTests
{
    private static async Task<(DispatchedTaskStore Store, Scheduler Scheduler, FixedGoalPlanQueryClient GoalPlanQueryClient)> CreateStackAsync()
    {
        var store = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var goalPlanQueryClient = new FixedGoalPlanQueryClient();
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), goalPlanQueryClient, new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        return (store, scheduler, goalPlanQueryClient);
    }

    // This real SQL Server table has no delete/cleanup method (matching every other real-infra
    // store in this codebase) and ExecutionCoordinator/Scheduler query it globally — retiring
    // leftover Ready rows via the store's own existing UpsertAsync (no new store method) keeps
    // "nothing is eligible"/"this specific task is dispatched next" assertions deterministic
    // against a shared table other tests/runs have left rows in.
    private static async Task ClearReadyQueueAsync(DispatchedTaskStore store)
    {
        var ready = await store.GetByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);
        foreach (var task in ready)
        {
            await store.UpsertAsync(task with { State = TaskLifecycleState.Cancelled }, CancellationToken.None);
        }
    }

    // Materializes a Ready Task via its own throwaway Scheduler instance (sharing the same
    // underlying store), then registers its (GoalId, PlanId) as "current" on the shared
    // goalPlanQueryClient so the caller-facing `scheduler`/`coordinator` under test — constructed
    // before this Plan/Goal pair existed — recognizes it as dispatchable under WP-025.4's
    // current-Plan filter.
    private static async Task<Guid> SeedReadyTaskAsync(DispatchedTaskStore store, FixedGoalPlanQueryClient goalPlanQueryClient)
    {
        await ClearReadyQueueAsync(store);

        var planTask = new PlanTask(Guid.NewGuid(), "Add a logging statement", ["logging"], []);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [planTask], 1, 1.0, null);
        goalPlanQueryClient.SetCurrentPlanId(plan.GoalId, plan.PlanId);

        var schedulerWithPlan = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedGoalPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        schedulerWithPlan.OnTaskCreated(planTask.TaskId, priority: 1);
        await schedulerWithPlan.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        await schedulerWithPlan.EvaluateReadinessAsync(CancellationToken.None);

        return planTask.TaskId;
    }

    [Fact]
    public async Task DispatchNextAsync_ReturnsNoEligibleTask_WhenNothingIsReady()
    {
        var (store, scheduler, _) = await CreateStackAsync();
        await ClearReadyQueueAsync(store);
        var taskStartedPublisher = new RecordingTaskStartedEventPublisher();
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), taskStartedPublisher);

        var result = await coordinator.DispatchNextAsync(CancellationToken.None);

        Assert.Equal(DispatchOutcome.NoEligibleTask, result.Outcome);
        Assert.Null(result.Task);
        Assert.Empty(taskStartedPublisher.PublishedTaskIds);
    }

    [Fact]
    public async Task DispatchNextAsync_TransitionsToRunning_AndPublishesTaskStarted_WhenProtectionAllows()
    {
        var (store, scheduler, goalPlanQueryClient) = await CreateStackAsync();
        var taskId = await SeedReadyTaskAsync(store, goalPlanQueryClient);
        var taskStartedPublisher = new RecordingTaskStartedEventPublisher();
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), taskStartedPublisher);

        var result = await coordinator.DispatchNextAsync(CancellationToken.None);

        Assert.Equal(DispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal(taskId, result.Task!.TaskId);
        Assert.Equal(TaskLifecycleState.Running, result.Task.State);
        Assert.NotNull(result.Task.RunningAt);
        Assert.Contains(taskId, taskStartedPublisher.PublishedTaskIds);

        var persisted = await store.GetByIdAsync(taskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Running, persisted!.State);
    }

    [Fact]
    public async Task DispatchNextAsync_LeavesTheTaskReady_AndDoesNotPublish_WhenProtectionDenies()
    {
        var (store, scheduler, goalPlanQueryClient) = await CreateStackAsync();
        var taskId = await SeedReadyTaskAsync(store, goalPlanQueryClient);
        var taskStartedPublisher = new RecordingTaskStartedEventPublisher();
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysDenyProtectionClient(), taskStartedPublisher);

        var result = await coordinator.DispatchNextAsync(CancellationToken.None);

        Assert.Equal(DispatchOutcome.ProtectionDenied, result.Outcome);
        Assert.Equal(taskId, result.Task!.TaskId);
        Assert.Empty(taskStartedPublisher.PublishedTaskIds);

        var persisted = await store.GetByIdAsync(taskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persisted!.State);
        Assert.Null(persisted.RunningAt);
    }

    // Principal Engineer Final Review, Finding 5: a genuine Protection infrastructure failure
    // (not a Deny verdict) must propagate honestly, never be swallowed, and must never leave the
    // Task transitioned to Running.
    [Fact]
    public async Task DispatchNextAsync_Propagates_AndDoesNotDispatch_WhenProtectionThrows()
    {
        var (store, scheduler, goalPlanQueryClient) = await CreateStackAsync();
        var taskId = await SeedReadyTaskAsync(store, goalPlanQueryClient);
        var taskStartedPublisher = new RecordingTaskStartedEventPublisher();
        var coordinator = new ExecutionCoordinator(scheduler, store, new ThrowingProtectionClient(), taskStartedPublisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DispatchNextAsync(CancellationToken.None));

        Assert.Empty(taskStartedPublisher.PublishedTaskIds);
        var persisted = await store.GetByIdAsync(taskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persisted!.State);
        Assert.Null(persisted.RunningAt);
    }

    // Principal Engineer Final Review, Finding 5: an already-cancelled token must propagate
    // OperationCanceledException honestly rather than silently returning NoEligibleTask.
    [Fact]
    public async Task DispatchNextAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        var (store, scheduler, goalPlanQueryClient) = await CreateStackAsync();
        await SeedReadyTaskAsync(store, goalPlanQueryClient);
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.DispatchNextAsync(alreadyCancelled.Token));
    }

    // Principal Engineer Final Review, Finding 5: repeated dispatch cycles must each advance a
    // different Task, in Priority Queue order, never re-selecting an already-Running Task.
    [Fact]
    public async Task DispatchNextAsync_DispatchesADifferentTask_OnEachSequentialCall()
    {
        var (store, _, _) = await CreateStackAsync();
        await ClearReadyQueueAsync(store);
        var lowPlanTask = new PlanTask(Guid.NewGuid(), "Lower priority task", ["logging"], []);
        var highPlanTask = new PlanTask(Guid.NewGuid(), "Higher priority task", ["logging"], []);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [lowPlanTask, highPlanTask], 2, 1.0, null);
        var schedulerWithPlan = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedGoalPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        schedulerWithPlan.OnTaskCreated(lowPlanTask.TaskId, priority: 1);
        schedulerWithPlan.OnTaskCreated(highPlanTask.TaskId, priority: 9);
        await schedulerWithPlan.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        await schedulerWithPlan.EvaluateReadinessAsync(CancellationToken.None);
        var coordinator = new ExecutionCoordinator(schedulerWithPlan, store, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());

        var first = await coordinator.DispatchNextAsync(CancellationToken.None);
        var second = await coordinator.DispatchNextAsync(CancellationToken.None);
        var third = await coordinator.DispatchNextAsync(CancellationToken.None);

        Assert.Equal(DispatchOutcome.Dispatched, first.Outcome);
        Assert.Equal(highPlanTask.TaskId, first.Task!.TaskId);
        Assert.Equal(DispatchOutcome.Dispatched, second.Outcome);
        Assert.Equal(lowPlanTask.TaskId, second.Task!.TaskId);
        Assert.Equal(DispatchOutcome.NoEligibleTask, third.Outcome);
    }

    // Principal Engineer Final Review, Finding 5: TaskStarted must be observably published only
    // after the Ready → Running write has already committed, never before.
    [Fact]
    public async Task DispatchNextAsync_PublishesTaskStarted_OnlyAfterTheTaskIsAlreadyRunning()
    {
        var (store, scheduler, goalPlanQueryClient) = await CreateStackAsync();
        await SeedReadyTaskAsync(store, goalPlanQueryClient);
        var stateCapturingPublisher = new StateCapturingTaskStartedEventPublisher(store);
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), stateCapturingPublisher);

        await coordinator.DispatchNextAsync(CancellationToken.None);

        var observedState = Assert.Single(stateCapturingPublisher.ObservedStatesAtPublishTime);
        Assert.Equal(TaskLifecycleState.Running, observedState);
    }
}
