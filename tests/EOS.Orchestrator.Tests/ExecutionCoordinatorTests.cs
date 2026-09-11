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
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), taskStartedPublisher, new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());

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
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), taskStartedPublisher, new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());

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
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysDenyProtectionClient(), taskStartedPublisher, new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());

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
        var coordinator = new ExecutionCoordinator(scheduler, store, new ThrowingProtectionClient(), taskStartedPublisher, new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());

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
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher(), new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());
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
        var coordinator = new ExecutionCoordinator(schedulerWithPlan, store, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher(), new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());

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
        var coordinator = new ExecutionCoordinator(scheduler, store, new AlwaysAllowProtectionClient(), stateCapturingPublisher, new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());

        await coordinator.DispatchNextAsync(CancellationToken.None);

        var observedState = Assert.Single(stateCapturingPublisher.ObservedStatesAtPublishTime);
        Assert.Equal(TaskLifecycleState.Running, observedState);
    }

    // ---------------------------------------------------------------------------------------
    // Post-Roadmap WP-A: ExecuteAndCompleteAsync — Constitution Part 6 §6.2 "Role executes",
    // Running → Review (+ TaskCompleted) on success, Running → Blocked (+ TaskBlocked) on every
    // non-success outcome. Real SQL Server store; hand-rolled doubles for the role and events.
    // ---------------------------------------------------------------------------------------

    private static async Task<(DispatchedTaskStore Store, DispatchedTask Running)> CreateRunningTaskAsync()
    {
        var (store, scheduler, goalPlanQueryClient) = await CreateStackAsync();
        await SeedReadyTaskAsync(store, goalPlanQueryClient);
        var dispatcher = new ExecutionCoordinator(
            scheduler, store, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher(),
            new NeverCalledTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(), new PassingUniversalGateClient());
        var dispatch = await dispatcher.DispatchNextAsync(CancellationToken.None);
        Assert.Equal(DispatchOutcome.Dispatched, dispatch.Outcome);
        return (store, dispatch.Task!);
    }

    private static ExecutionCoordinator CreateCompleter(
        DispatchedTaskStore store,
        IProtectionClient protection,
        ITaskExecutionClient executor,
        ITaskCompletedEventPublisher completed,
        ITaskBlockedEventPublisher blocked,
        IUniversalGateClient? gates = null)
    {
        var scheduler = new Scheduler(
            store, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        return new ExecutionCoordinator(scheduler, store, protection, new RecordingTaskStartedEventPublisher(), executor, completed, blocked, gates ?? new PassingUniversalGateClient());
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_TransitionsToReview_AndPublishesTaskCompletedWithTheEvidence_WhenProtectionAllows()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var runningBaseline = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        var executor = new FixedEvidenceTaskExecutionClient("artifact:" + new string('a', 64));
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var coordinator = CreateCompleter(store, new AlwaysAllowProtectionClient(), executor, completed, blocked);

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.Equal(TaskLifecycleState.Review, result.Task.State);
        Assert.Equal(["artifact:" + new string('a', 64)], result.EvidenceRefs);
        Assert.Null(result.Error);
        Assert.Equal([running.TaskId], executor.ExecutedTaskIds);
        var published = Assert.Single(completed.Published);
        Assert.Equal(running.TaskId, published.TaskId);
        Assert.Equal(result.EvidenceRefs, published.EvidenceRefs);
        Assert.Empty(blocked.Published);
        var persisted = await store.GetByIdAsync(running.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Review, persisted!.State);
        Assert.Null(persisted.BlockedReason);
        Assert.Equal(runningBaseline - 1, await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_PublishesTaskCompleted_OnlyAfterReviewIsAlreadyPersisted()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var stateCapturing = new StateCapturingTaskCompletedEventPublisher(store);
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new FixedEvidenceTaskExecutionClient("artifact:" + new string('b', 64)),
            stateCapturing, new RecordingTaskBlockedEventPublisher());

        await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Review, Assert.Single(stateCapturing.ObservedStatesAtPublishTime));
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_BlocksTheTask_KeepsTheEvidence_AndNeverPublishesTaskCompleted_WhenProtectionDenies()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var runningBaseline = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var evidence = "artifact:" + new string('c', 64);
        var coordinator = CreateCompleter(store, new AlwaysDenyProtectionClient(), new FixedEvidenceTaskExecutionClient(evidence), completed, blocked);

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.ProtectionDenied, result.Outcome);
        Assert.Equal(TaskLifecycleState.Blocked, result.Task.State);
        Assert.Equal([evidence], result.EvidenceRefs);
        Assert.Empty(completed.Published);
        var publishedBlock = Assert.Single(blocked.Published);
        Assert.Equal(running.TaskId, publishedBlock.TaskId);
        Assert.Contains("TaskCompletion denied", publishedBlock.Reason);
        Assert.Contains(evidence, publishedBlock.Reason);
        var persisted = await store.GetByIdAsync(running.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Equal(publishedBlock.Reason, persisted.BlockedReason);
        Assert.Equal(runningBaseline - 1, await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_BlocksTheTask_AndNeverPublishesTaskCompleted_WhenTheRoleFails()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var runningBaseline = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var executor = new ThrowingTaskExecutionClient("The produced diff is not a valid, in-scope unified diff: no @@ hunk");
        var coordinator = CreateCompleter(store, new AlwaysAllowProtectionClient(), executor, completed, blocked);

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.ExecutionFailed, result.Outcome);
        Assert.Equal(TaskLifecycleState.Blocked, result.Task.State);
        Assert.Empty(result.EvidenceRefs);
        Assert.Contains("no @@ hunk", result.Error);
        Assert.Empty(completed.Published);
        var publishedBlock = Assert.Single(blocked.Published);
        Assert.StartsWith("Execution failed:", publishedBlock.Reason);
        var persisted = await store.GetByIdAsync(running.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Contains("no @@ hunk", persisted.BlockedReason);
        Assert.Equal(runningBaseline - 1, await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_BlocksTheTask_WhenTheRoleReturnsNoEvidence()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var completed = new RecordingTaskCompletedEventPublisher();
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new FixedEvidenceTaskExecutionClient(), completed, new RecordingTaskBlockedEventPublisher());

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.ExecutionFailed, result.Outcome);
        Assert.Empty(completed.Published);
        Assert.Equal(TaskLifecycleState.Blocked, (await store.GetByIdAsync(running.TaskId, CancellationToken.None))!.State);
    }

    // In-process cancellation: the Running → Blocked write and TaskBlocked must complete (with a
    // non-cancelled token) before the cancellation is rethrown; the Task never remains Running.
    [Fact]
    public async Task ExecuteAndCompleteAsync_BlocksTheTask_PublishesTaskBlocked_ThenRethrows_OnInProcessCancellation()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var runningBaseline = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new CancellingTaskExecutionClient(cancellation), completed, blocked);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ExecuteAndCompleteAsync(running, cancellation.Token));

        Assert.Empty(completed.Published);
        var publishedBlock = Assert.Single(blocked.Published);
        Assert.Equal("Execution cancelled before completion.", publishedBlock.Reason);
        var persisted = await store.GetByIdAsync(running.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Equal("Execution cancelled before completion.", persisted.BlockedReason);
        Assert.Equal(runningBaseline - 1, await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None));
    }

    // Compensating persistence failure during cancellation: neither exception is swallowed.
    [Fact]
    public async Task ExecuteAndCompleteAsync_PreservesBothExceptions_WhenTheCompensatingBlockedWriteFailsDuringCancellation()
    {
        var (store, running) = await CreateRunningTaskAsync();
        using var cancellation = new CancellationTokenSource();
        var throwingPublisher = new ThrowingTaskBlockedEventPublisher();
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new CancellingTaskExecutionClient(cancellation),
            new RecordingTaskCompletedEventPublisher(), throwingPublisher);

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => coordinator.ExecuteAndCompleteAsync(running, cancellation.Token));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.IsAssignableFrom<OperationCanceledException>(aggregate.InnerExceptions[0]);
        Assert.IsType<InvalidOperationException>(aggregate.InnerExceptions[1]);
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_Throws_AndChangesNothing_WhenTheTaskIsNotRunning()
    {
        var (store, _, _) = await CreateStackAsync();
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var executor = new FixedEvidenceTaskExecutionClient("artifact:" + new string('d', 64));
        var coordinator = CreateCompleter(store, new AlwaysAllowProtectionClient(), executor, completed, blocked);
        var ready = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "not running", [], [], 1, TaskLifecycleState.Ready,
            SchedulingMode.Immediate, null, null, false, 0, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAndCompleteAsync(ready, CancellationToken.None));

        Assert.Empty(executor.ExecutedTaskIds);
        Assert.Empty(completed.Published);
        Assert.Empty(blocked.Published);
    }

    // ---------------------------------------------------------------------------------------
    // ADR-009: Universal Gates 1–2 (Constitution §0.8.1) between evidence registration and the
    // TaskCompletion validation. Gate failure is §0.8.3's blocking status; never TaskCompleted.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAndCompleteAsync_EvaluatesTheUniversalGates_WithTheRegisteredEvidence_BeforeReview()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var evidence = "artifact:" + new string('e', 64);
        var gates = new PassingUniversalGateClient();
        var completed = new RecordingTaskCompletedEventPublisher();
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new FixedEvidenceTaskExecutionClient(evidence), completed,
            new RecordingTaskBlockedEventPublisher(), gates);

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        var evaluated = Assert.Single(gates.Evaluated);
        Assert.Equal(running.TaskId, evaluated.TaskId);
        Assert.Equal([evidence], evaluated.EvidenceRefs);
        Assert.Single(completed.Published);
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_BlocksTheTask_KeepsTheEvidence_AndNeverPublishesTaskCompleted_WhenAUniversalGateFails()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var runningBaseline = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        var evidence = "artifact:" + new string('f', 64);
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var gates = new FailingUniversalGateClient("Universal Gate 1 (build/static analysis) failed: error CS1519");
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new FixedEvidenceTaskExecutionClient(evidence), completed, blocked, gates);

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.ExecutionFailed, result.Outcome);
        Assert.Equal(TaskLifecycleState.Blocked, result.Task.State);
        Assert.Equal([evidence], result.EvidenceRefs);
        Assert.Equal(1, gates.CallCount);
        Assert.Empty(completed.Published);
        var publishedBlock = Assert.Single(blocked.Published);
        Assert.Equal(running.TaskId, publishedBlock.TaskId);
        Assert.StartsWith("Universal Gate failure:", publishedBlock.Reason);
        Assert.Contains("Universal Gate 1", publishedBlock.Reason);
        Assert.Contains("CS1519", publishedBlock.Reason);
        Assert.Contains(evidence, publishedBlock.Reason);
        var persisted = await store.GetByIdAsync(running.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Equal(publishedBlock.Reason, persisted.BlockedReason);
        Assert.Equal(runningBaseline - 1, await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_BlocksTheTask_AndNeverPublishesTaskCompleted_WhenTheUniversalGatesCannotRun()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var evidence = "artifact:" + new string('1', 64);
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new FixedEvidenceTaskExecutionClient(evidence), completed, blocked,
            new ThrowingUniversalGateClient("could not create the isolated copy"));

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.ExecutionFailed, result.Outcome);
        Assert.Equal(TaskLifecycleState.Blocked, result.Task.State);
        Assert.Empty(completed.Published);
        var publishedBlock = Assert.Single(blocked.Published);
        Assert.StartsWith("Universal Gate evaluation failed:", publishedBlock.Reason);
        Assert.Contains("could not create the isolated copy", publishedBlock.Reason);
        Assert.Contains(evidence, publishedBlock.Reason);
        var persisted = await store.GetByIdAsync(running.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_NeverRunsTheUniversalGates_WhenTheRoleFails()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new ThrowingTaskExecutionClient(), new RecordingTaskCompletedEventPublisher(),
            new RecordingTaskBlockedEventPublisher(), new NeverCalledUniversalGateClient());

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        Assert.Equal(ExecutionOutcome.ExecutionFailed, result.Outcome);
        Assert.Equal(TaskLifecycleState.Blocked, result.Task.State);
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_NeverValidatesTaskCompletion_WhenAUniversalGateFails()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var protection = new DenyActionTypeProtectionClient("TaskCompletion");
        var coordinator = CreateCompleter(
            store, protection, new FixedEvidenceTaskExecutionClient("artifact:" + new string('2', 64)),
            new RecordingTaskCompletedEventPublisher(), new RecordingTaskBlockedEventPublisher(),
            new FailingUniversalGateClient("Universal Gate 2 (unit tests) failed: 1 failed"));

        var result = await coordinator.ExecuteAndCompleteAsync(running, CancellationToken.None);

        // The gate failure is the reported root cause — not the (never reached) TaskCompletion denial.
        Assert.Equal(ExecutionOutcome.ExecutionFailed, result.Outcome);
        Assert.Contains("Universal Gate 2", result.Task.BlockedReason);
        Assert.DoesNotContain("TaskCompletion denied", result.Task.BlockedReason);
    }

    [Fact]
    public async Task ExecuteAndCompleteAsync_PersistsBlocked_PublishesTaskBlocked_AndRethrows_WhenCancelledDuringTheUniversalGates()
    {
        var (store, running) = await CreateRunningTaskAsync();
        var runningBaseline = await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var completed = new RecordingTaskCompletedEventPublisher();
        var blocked = new RecordingTaskBlockedEventPublisher();
        var coordinator = CreateCompleter(
            store, new AlwaysAllowProtectionClient(), new FixedEvidenceTaskExecutionClient("artifact:" + new string('3', 64)),
            completed, blocked, new CancellingUniversalGateClient(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ExecuteAndCompleteAsync(running, cancellation.Token));

        Assert.Empty(completed.Published);
        var publishedBlock = Assert.Single(blocked.Published);
        Assert.Equal("Execution cancelled during Universal Gate evaluation.", publishedBlock.Reason);
        var persisted = await store.GetByIdAsync(running.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Equal(runningBaseline - 1, await store.CountByStateAsync(TaskLifecycleState.Running, CancellationToken.None));
    }

    private sealed class ThrowingTaskBlockedEventPublisher : ITaskBlockedEventPublisher
    {
        public void PublishTaskBlocked(Guid taskId, string reason) =>
            throw new InvalidOperationException("Event backbone unavailable.");
    }
}
