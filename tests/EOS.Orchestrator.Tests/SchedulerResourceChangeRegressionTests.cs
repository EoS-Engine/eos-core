using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

/// <summary>
/// WP-025.6 Board Ruling: Planning-Execution-Engine-Specification-v1.0 §16.3's "Replanning After
/// Resource Changes" requirement is a required *behavior*, not a mandatory new event/trigger
/// mechanism — <see cref="Scheduler.SelectNextDispatchableTaskAsync"/> already reads
/// <see cref="IResourceManagementClient"/> fresh, with no caching, on every invocation, so a
/// resource-state change is reflected on the very next Scheduler evaluation with no explicit
/// signal required. This test proves that behavior directly; it does not introduce, simulate, or
/// imply any new production trigger.
/// </summary>
public class SchedulerResourceChangeRegressionTests
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

    private static DispatchedTask NewTask(Guid goalId, Guid planId, TaskLifecycleState state) => new(
        TaskId: Guid.NewGuid(),
        PlanId: planId,
        GoalId: goalId,
        Description: "Resource-change regression task",
        CompetencyRequirements: ["logging"],
        DependsOnTaskIds: [],
        Priority: 1,
        State: state,
        SchedulingMode: SchedulingMode.Immediate,
        NotBefore: null,
        RunningAt: state == TaskLifecycleState.Running ? DateTimeOffset.UtcNow : null,
        EventObserved: false,
        RetryCount: 0,
        BlockedReason: null);

    [Fact]
    public async Task SelectNextDispatchableTaskAsync_ReflectsAChangedResourceTier_OnTheVeryNextCall_WithNoExplicitTriggerEvent()
    {
        var store = await CreateStoreAsync();
        await ClearReadyQueueAsync(store);
        var goalId = Guid.NewGuid();
        var currentPlanId = Guid.NewGuid();
        var oldPlanId = Guid.NewGuid();

        var currentPlanTask = NewTask(goalId, currentPlanId, TaskLifecycleState.Ready);
        var oldPlanTask = NewTask(goalId, oldPlanId, TaskLifecycleState.Ready);
        var runningTask = NewTask(goalId, currentPlanId, TaskLifecycleState.Running);
        await store.UpsertAsync(currentPlanTask, CancellationToken.None);
        await store.UpsertAsync(oldPlanTask, CancellationToken.None);
        await store.UpsertAsync(runningTask, CancellationToken.None);

        // Resource state A: Critical — nothing is dispatchable, structurally.
        var resourceManagementClient = new MutableTierResourceManagementClient(CapacityTier.Critical);
        var scheduler = new Scheduler(
            store, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient((goalId, currentPlanId)),
            resourceManagementClient, concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

        var selectedUnderState_A = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);
        Assert.Null(selectedUnderState_A);

        // Resource state changes to B (Safe) via the SAME resource-client instance the Scheduler
        // already holds a reference to — no event, callback, or new Scheduler/store instance.
        resourceManagementClient.Tier = CapacityTier.Safe;

        // The very next Scheduler evaluation — same Scheduler instance, same store — observes
        // the new tier immediately, proving fresh (not cached) evaluation.
        var selectedUnderState_B = await scheduler.SelectNextDispatchableTaskAsync(CancellationToken.None);

        Assert.NotNull(selectedUnderState_B);
        Assert.Equal(currentPlanTask.TaskId, selectedUnderState_B.TaskId);

        // Existing WP-025.4 current-Plan filtering is preserved under the changed resource state:
        // the old-Plan Ready Task is still never selected.
        Assert.NotEqual(oldPlanTask.TaskId, selectedUnderState_B.TaskId);
        var persistedOldPlanTask = await store.GetByIdAsync(oldPlanTask.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persistedOldPlanTask!.State);
        Assert.Equal(oldPlanId, persistedOldPlanTask.PlanId);

        // No Plan was created, no Goal.PlanId/DispatchedTask.PlanId was ever mutated — the
        // current-Plan Task's own PlanId is exactly what it was seeded with.
        var persistedCurrentPlanTask = await store.GetByIdAsync(currentPlanTask.TaskId, CancellationToken.None);
        Assert.Equal(currentPlanId, persistedCurrentPlanTask!.PlanId);

        // Running Task is completely untouched by either evaluation.
        var persistedRunningTask = await store.GetByIdAsync(runningTask.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Running, persistedRunningTask!.State);
        Assert.Equal(runningTask.RunningAt, persistedRunningTask.RunningAt);
        Assert.Equal(runningTask.PlanId, persistedRunningTask.PlanId);
    }
}
