using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

public class SchedulerTests
{
    private static async Task<DispatchedTaskStore> CreateStoreAsync()
    {
        var store = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    // This real SQL Server table has no delete/cleanup method (matching every other real-infra
    // store in this codebase) and Scheduler queries it globally (there is only one Priority
    // Queue, matching real Scheduler semantics) — a test asserting "no task is eligible" would
    // otherwise be polluted by Ready rows any other test in this suite has ever left behind.
    // Retiring them via the store's own existing UpsertAsync (no new store method) keeps that
    // assumption valid without inventing cleanup infrastructure.
    private static async Task ClearReadyQueueAsync(DispatchedTaskStore store)
    {
        var ready = await store.GetByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);
        foreach (var task in ready)
        {
            await store.UpsertAsync(task with { State = TaskLifecycleState.Cancelled }, CancellationToken.None);
        }
    }

    private static Plan NewPlan(params PlanTask[] tasks) => new(
        PlanId: Guid.NewGuid(),
        GoalId: Guid.NewGuid(),
        Tasks: tasks,
        EstimatedResourceCost: tasks.Length,
        RiskAdjustedConfidenceScore: 1.0);

    private static PlanTask NewPlanTask(Guid[]? dependsOnTaskIds = null) => new(
        TaskId: Guid.NewGuid(),
        Description: "Add a logging statement",
        CompetencyRequirements: ["logging"],
        DependsOnTaskIds: dependsOnTaskIds ?? []);

    [Fact]
    public async Task OnPlannerGeneratedAsync_MaterializesADispatchedTaskPerPlanTask_InPlannedState()
    {
        var store = await CreateStoreAsync();
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

        scheduler.OnTaskCreated(planTask.TaskId, priority: 5);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);

        var persisted = await store.GetByIdAsync(planTask.TaskId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(TaskLifecycleState.Planned, persisted.State);
        Assert.Equal(plan.PlanId, persisted.PlanId);
        Assert.Equal(plan.GoalId, persisted.GoalId);
        Assert.Equal(5, persisted.Priority);
        Assert.Equal(SchedulingMode.Immediate, persisted.SchedulingMode);
    }

    [Fact]
    public async Task OnPlannerGeneratedAsync_Throws_WhenATaskHasNoCachedPriority()
    {
        var store = await CreateStoreAsync();
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None));
    }

    // Principal Engineer Final Review, Blocking Finding 1: reproduces a genuine partial
    // materialization failure — one task's priority is cached (so it fully reaches Planned)
    // before a second task's missing cached priority throws. The already-materialized task must
    // not be left permanently invisible in Created; it must be rolled back to Cancelled, and the
    // caller must still receive the original, honest exception (never swallowed).
    [Fact]
    public async Task OnPlannerGeneratedAsync_CancelsAlreadyMaterializedTasks_WhenALaterTaskFailsToMaterialize()
    {
        var store = await CreateStoreAsync();
        var succeedingTask = NewPlanTask();
        var failingTask = NewPlanTask();
        var plan = NewPlan(succeedingTask, failingTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        // Only the first task's priority is cached — the second's lookup fails partway through
        // the loop, after the first has already been fully written through to Planned.
        scheduler.OnTaskCreated(succeedingTask.TaskId, priority: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None));

        var persistedSucceeding = await store.GetByIdAsync(succeedingTask.TaskId, CancellationToken.None);
        Assert.NotNull(persistedSucceeding);
        Assert.Equal(TaskLifecycleState.Cancelled, persistedSucceeding.State);

        // The second task's own write never happened (the throw occurs before its first
        // UpsertAsync call), so it is correctly absent, not merely uncancelled.
        var persistedFailing = await store.GetByIdAsync(failingTask.TaskId, CancellationToken.None);
        Assert.Null(persistedFailing);
    }

    // Principal Engineer Final Review, Blocking Finding 1: cancellation is a realistic
    // materialization-failure trigger — an already-cancelled token must propagate
    // OperationCanceledException honestly (never swallowed, never converted into false success)
    // and must never leave a task stuck in Created/Planned.
    [Fact]
    public async Task OnPlannerGeneratedAsync_PropagatesCancellation_AndLeavesNoStuckTask()
    {
        var store = await CreateStoreAsync();
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        scheduler.OnTaskCreated(planTask.TaskId, priority: 1);
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scheduler.OnPlannerGeneratedAsync(plan.PlanId, alreadyCancelled.Token));

        var persisted = await store.GetByIdAsync(planTask.TaskId, CancellationToken.None);
        Assert.True(persisted is null || persisted.State == TaskLifecycleState.Cancelled);
    }

    [Fact]
    public async Task OnPlannerGeneratedAsync_Throws_WhenThePlanDoesNotExist()
    {
        var store = await CreateStoreAsync();
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.OnPlannerGeneratedAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateReadinessAsync_TransitionsPlannedToReady_WhenThereAreNoDependencies()
    {
        var store = await CreateStoreAsync();
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        scheduler.OnTaskCreated(planTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);

        var readyNow = await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        Assert.Contains(readyNow, task => task.TaskId == planTask.TaskId);
        var persisted = await store.GetByIdAsync(planTask.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persisted!.State);
    }

    [Fact]
    public async Task EvaluateReadinessAsync_DoesNotTransition_WhenADependencyHasNotReachedVerifiedOrReleased()
    {
        var store = await CreateStoreAsync();
        var dependencyTask = NewPlanTask();
        var dependentTask = NewPlanTask(dependsOnTaskIds: [dependencyTask.TaskId]);
        var plan = NewPlan(dependencyTask, dependentTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        scheduler.OnTaskCreated(dependencyTask.TaskId, priority: 1);
        scheduler.OnTaskCreated(dependentTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);

        var readyNow = await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        Assert.DoesNotContain(readyNow, task => task.TaskId == dependentTask.TaskId);
        var persisted = await store.GetByIdAsync(dependentTask.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Planned, persisted!.State);
    }

    [Theory]
    [InlineData(TaskLifecycleState.Verified)]
    [InlineData(TaskLifecycleState.Released)]
    public async Task EvaluateReadinessAsync_Transitions_WhenEveryDependencyHasReachedVerifiedOrReleased(TaskLifecycleState dependencyState)
    {
        var store = await CreateStoreAsync();
        var dependencyTask = NewPlanTask();
        var dependentTask = NewPlanTask(dependsOnTaskIds: [dependencyTask.TaskId]);
        var plan = NewPlan(dependencyTask, dependentTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        scheduler.OnTaskCreated(dependencyTask.TaskId, priority: 1);
        scheduler.OnTaskCreated(dependentTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        var storedDependency = await store.GetByIdAsync(dependencyTask.TaskId, CancellationToken.None);
        await store.UpsertAsync(storedDependency! with { State = dependencyState }, CancellationToken.None);

        var readyNow = await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        Assert.Contains(readyNow, task => task.TaskId == dependentTask.TaskId);
    }

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_ReturnsNull_WhenNoTasksAreReady()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_ReturnsTheHighestPriorityReadyTask()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var low = NewPlanTask();
        var high = NewPlanTask();
        var plan = NewPlan(low, high);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        scheduler.OnTaskCreated(low.TaskId, priority: 1);
        scheduler.OnTaskCreated(high.TaskId, priority: 9);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Equal(high.TaskId, selected!.TaskId);
    }

    [Theory]
    [InlineData(CapacityTier.Critical)]
    [InlineData(CapacityTier.Emergency)]
    public async Task SelectNextDispatchableTaskAsync_ReturnsNull_WhenResourceTierIsCriticalOrEmergency(CapacityTier tier)
    {
        var store = await CreateStoreAsync();
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(tier), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        scheduler.OnTaskCreated(planTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selected);
    }

    [Theory]
    [InlineData(CapacityTier.Safe)]
    [InlineData(CapacityTier.Warning)]
    public async Task SelectNextDispatchableTaskAsync_ReturnsTheTask_WhenResourceTierIsSafeOrWarning(CapacityTier tier)
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(tier), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        scheduler.OnTaskCreated(planTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Equal(planTask.TaskId, selected!.TaskId);
    }

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_ReturnsNull_WhenTheConcurrencyCeilingIsReached()
    {
        var store = await CreateStoreAsync();
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1, dailyCapacity: 100);
        scheduler.OnTaskCreated(planTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        await store.UpsertAsync(
            new DispatchedTask(Guid.NewGuid(), plan.PlanId, plan.GoalId, "Already running", [], [], 1, TaskLifecycleState.Running, SchedulingMode.Immediate, null, DateTimeOffset.UtcNow, false),
            CancellationToken.None);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_ReturnsNull_WhenTheDailyCapacityIsReached()
    {
        var store = await CreateStoreAsync();
        var planTask = NewPlanTask();
        var plan = NewPlan(planTask);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(plan), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 4, dailyCapacity: 1);
        scheduler.OnTaskCreated(planTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(plan.PlanId, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        await store.UpsertAsync(
            new DispatchedTask(Guid.NewGuid(), plan.PlanId, plan.GoalId, "Already dispatched today", [], [], 1, TaskLifecycleState.Running, SchedulingMode.Immediate, null, DateTimeOffset.UtcNow, false),
            CancellationToken.None);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selected);
    }

    [Fact]
    public async Task ScheduleAsync_MaterializesADelayedTask_IneligibleUntilItsNotBeforeTimePasses()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var futureTask = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Delayed task", ["logging"], [], 5,
            TaskLifecycleState.Created, SchedulingMode.Delayed, DateTimeOffset.UtcNow.AddHours(1), null, false);

        await scheduler.ScheduleAsync(futureTask, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var selectedWhileFuture = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selectedWhileFuture);

        var pastTask = futureTask with { NotBefore = DateTimeOffset.UtcNow.AddHours(-1) };
        await store.UpsertAsync(pastTask with { State = TaskLifecycleState.Ready }, CancellationToken.None);
        var selectedAfterDue = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Equal(futureTask.TaskId, selectedAfterDue!.TaskId);
    }

    // Principal Engineer Final Review, Blocking Finding 2: Periodic Execution (§14) is, like
    // Delayed/Scheduled, a timestamp gate — this proves it is genuinely gated by NotBefore, not
    // silently treated as Immediate.
    [Fact]
    public async Task ScheduleAsync_MaterializesAPeriodicTask_IneligibleUntilItsNotBeforeTimePasses()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var futureOccurrence = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Periodic sweep occurrence", ["logging"], [], 5,
            TaskLifecycleState.Created, SchedulingMode.Periodic, DateTimeOffset.UtcNow.AddHours(1), null, false);

        await scheduler.ScheduleAsync(futureOccurrence, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var selectedWhileFuture = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selectedWhileFuture);

        var dueOccurrence = futureOccurrence with { NotBefore = DateTimeOffset.UtcNow.AddHours(-1), State = TaskLifecycleState.Ready };
        await store.UpsertAsync(dueOccurrence, CancellationToken.None);
        var selectedAfterDue = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Equal(futureOccurrence.TaskId, selectedAfterDue!.TaskId);
    }

    // Principal Engineer Final Review, Blocking Finding 2: Event-Driven Execution (§14) must be
    // ineligible until the required event is explicitly observed — never silently eligible like
    // Immediate.
    [Fact]
    public async Task ScheduleAsync_MaterializesAnEventDrivenTask_IneligibleUntilTheEventIsObserved()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var task = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Runs after IncidentDetected", ["ops"], [], 5,
            TaskLifecycleState.Created, SchedulingMode.EventDriven, null, null, false);

        await scheduler.ScheduleAsync(task, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var selectedBeforeEvent = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selectedBeforeEvent);
        Assert.False((await store.GetByIdAsync(task.TaskId, CancellationToken.None))!.EventObserved);

        await scheduler.MarkEventObserved(task.TaskId, CancellationToken.None);
        var selectedAfterEvent = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Equal(task.TaskId, selectedAfterEvent!.TaskId);
    }

    // Test-coverage closure (roadmap WP-024 Test Verification: "Unit tests for each Scheduling
    // mode") — Scheduled Execution (§14) uses the identical NotBefore timestamp gate as
    // Delayed/Periodic (see IsEligibleForItsSchedulingMode's own switch arm); this proves that
    // shared code path under the Scheduled enum value specifically, not merely by association.
    [Fact]
    public async Task ScheduleAsync_MaterializesAScheduledTask_IneligibleUntilItsNotBeforeTimePasses()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var futureReview = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Quarterly-cycle-aligned review", ["governance"], [], 5,
            TaskLifecycleState.Created, SchedulingMode.Scheduled, DateTimeOffset.UtcNow.AddHours(1), null, false);

        await scheduler.ScheduleAsync(futureReview, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var selectedWhileFuture = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Null(selectedWhileFuture);

        var dueReview = futureReview with { NotBefore = DateTimeOffset.UtcNow.AddHours(-1) };
        await store.UpsertAsync(dueReview with { State = TaskLifecycleState.Ready }, CancellationToken.None);
        var selectedAfterDue = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Equal(futureReview.TaskId, selectedAfterDue!.TaskId);
    }

    // Test-coverage closure: Background Execution (§14) is eligible only during declared
    // Maintenance Windows or genuine idle CPU capacity — currently represented by
    // WithinMaintenanceWindow()'s disclosed always-true stub (WP-022 Implementation Plan
    // Decision D10, no real Maintenance Window data source exists anywhere in this codebase).
    // This proves that stub's actual, current eligibility outcome for a Background-mode Task,
    // rather than merely asserting the constant in isolation.
    [Fact]
    public async Task ScheduleAsync_MaterializesABackgroundTask_EligibleUnderTheCurrentMaintenanceWindowState()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var task = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Background maintenance sweep", ["ops"], [], 5,
            TaskLifecycleState.Created, SchedulingMode.Background, null, null, false);

        await scheduler.ScheduleAsync(task, CancellationToken.None);
        var readyNow = await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        Assert.Contains(readyNow, readyTask => readyTask.TaskId == task.TaskId);

        var selected = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.Equal(task.TaskId, selected!.TaskId);
    }

    // Test-coverage closure: §14 states Idle-Time Execution has an "Identical eligibility
    // condition to Background Execution" — this proves that equivalence directly, rather than
    // merely asserting IdleTime's own outcome in isolation: both a Background-mode and an
    // IdleTime-mode Task, materialized under the same system state, must both reach Ready and
    // both be eligible.
    [Fact]
    public async Task ScheduleAsync_MaterializesAnIdleTimeTask_HasEligibilityEquivalentToBackground()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var backgroundTask = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Background occurrence", ["ops"], [], 1,
            TaskLifecycleState.Created, SchedulingMode.Background, null, null, false);
        var idleTimeTask = new DispatchedTask(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Idle-time occurrence", ["ops"], [], 1,
            TaskLifecycleState.Created, SchedulingMode.IdleTime, null, null, false);

        await scheduler.ScheduleAsync(backgroundTask, CancellationToken.None);
        await scheduler.ScheduleAsync(idleTimeTask, CancellationToken.None);
        var readyNow = await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        Assert.Contains(readyNow, readyTask => readyTask.TaskId == backgroundTask.TaskId);
        Assert.Contains(readyNow, readyTask => readyTask.TaskId == idleTimeTask.TaskId);
    }

    [Fact]
    public async Task MarkEventObserved_Throws_WhenTheTaskDoesNotExist()
    {
        var store = await CreateStoreAsync();
        var scheduler = new Scheduler(store, new FixedPlanQueryClient(), new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scheduler.MarkEventObserved(Guid.NewGuid(), CancellationToken.None));
    }
}
