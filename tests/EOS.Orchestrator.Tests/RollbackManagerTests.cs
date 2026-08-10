using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Orchestrator.Tests;

public class RollbackManagerTests
{
    private static async Task<DispatchedTaskStore> CreateStoreAsync()
    {
        var store = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static DispatchedTask NewTask(TaskLifecycleState state, int retryCount = 0) => new(
        TaskId: Guid.NewGuid(),
        PlanId: Guid.NewGuid(),
        GoalId: Guid.NewGuid(),
        Description: "Rollback-eligible task",
        CompetencyRequirements: ["logging"],
        DependsOnTaskIds: [],
        Priority: 1,
        State: state,
        SchedulingMode: SchedulingMode.Immediate,
        NotBefore: null,
        RunningAt: state == TaskLifecycleState.Running || retryCount > 0 ? DateTimeOffset.UtcNow : null,
        EventObserved: false,
        RetryCount: retryCount,
        BlockedReason: state == TaskLifecycleState.Blocked ? "Gate failure: coverage below threshold" : null);

    // ---------- Single Task ----------

    [Fact]
    public async Task RollbackAsync_TransitionsToTheDefinedTargetState_AndPublishesRollbackExecuted()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(store, publisher);

        var result = await rollbackManager.RollbackAsync(task, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Planned, result.State);
        Assert.Equal(task.TaskId, result.TaskId);
        Assert.Contains((task.TaskId, TaskLifecycleState.Planned.ToString()), publisher.Published);

        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Planned, persisted!.State);
    }

    // Constitution Part 6 §6.2's Rollback Path column, one case per supported source state.
    // Running is parametrized separately below (table-ambiguous by source transition, resolved
    // via RetryCount — see RollbackManager.ResolveRollbackTargetState's own doc comment).
    [Theory]
    [InlineData(TaskLifecycleState.Planned, TaskLifecycleState.Cancelled)]
    [InlineData(TaskLifecycleState.Ready, TaskLifecycleState.Planned)]
    [InlineData(TaskLifecycleState.Waiting, TaskLifecycleState.Running)]
    [InlineData(TaskLifecycleState.Review, TaskLifecycleState.Running)]
    [InlineData(TaskLifecycleState.Testing, TaskLifecycleState.Review)]
    [InlineData(TaskLifecycleState.Verified, TaskLifecycleState.Testing)]
    [InlineData(TaskLifecycleState.Released, TaskLifecycleState.Verified)]
    public async Task RollbackAsync_ResolvesTheExactConstitutionSection6Point2TargetState(
        TaskLifecycleState source, TaskLifecycleState expectedTarget)
    {
        var store = await CreateStoreAsync();
        var task = NewTask(source);
        await store.UpsertAsync(task, CancellationToken.None);
        var rollbackManager = new RollbackManager(store, new RecordingRollbackExecutedEventPublisher());

        var result = await rollbackManager.RollbackAsync(task, CancellationToken.None);

        Assert.Equal(expectedTarget, result.State);
    }

    [Theory]
    [InlineData(0, TaskLifecycleState.Ready)]   // Ready → Running: rollback → Ready
    [InlineData(1, TaskLifecycleState.Blocked)] // Retry → Running: rollback → Blocked
    public async Task RollbackAsync_DisambiguatesRunning_UsingRetryCount(int retryCount, TaskLifecycleState expectedTarget)
    {
        var store = await CreateStoreAsync();
        var task = NewTask(TaskLifecycleState.Running, retryCount);
        await store.UpsertAsync(task, CancellationToken.None);
        var rollbackManager = new RollbackManager(store, new RecordingRollbackExecutedEventPublisher());

        var result = await rollbackManager.RollbackAsync(task, CancellationToken.None);

        Assert.Equal(expectedTarget, result.State);
        // CodeRabbit PR #22 round 1: rolling back to Blocked must record why, matching
        // RetryManager's own BlockedReason discipline.
        if (expectedTarget == TaskLifecycleState.Blocked)
        {
            Assert.NotNull(result.BlockedReason);
        }
    }

    // Constitution §6.2's Running → Blocked Rollback Path is genuinely two-valued ("→ Running
    // (after fix) or → Cancelled") with no derivable field to choose between them — refused
    // rather than guessed, per FR-PE9's own anticipated fallback (non-rollback-able by design).
    [Fact]
    public async Task RollbackAsync_Throws_AndDoesNotPersistOrPublish_ForTheAmbiguousBlockedRollbackPath()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(TaskLifecycleState.Blocked);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(store, publisher);

        await Assert.ThrowsAsync<NotSupportedException>(() => rollbackManager.RollbackAsync(task, CancellationToken.None));

        Assert.Empty(publisher.Published);
        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
    }

    [Fact]
    public async Task RollbackAsync_PublishesTheExactPayload_TaskIdAndRollbackPathUsedOnly()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(TaskLifecycleState.Testing);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(store, publisher);

        await rollbackManager.RollbackAsync(task, CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(task.TaskId, published.TaskId);
        Assert.Equal(TaskLifecycleState.Review.ToString(), published.RollbackPathUsed);
    }

    [Fact]
    public async Task RollbackAsync_PublishesRollbackExecuted_OnlyAfterTheRollbackTransitionIsAlreadyPersisted()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new StateCapturingRollbackExecutedEventPublisher(store);
        var rollbackManager = new RollbackManager(store, publisher);

        await rollbackManager.RollbackAsync(task, CancellationToken.None);

        var observedState = Assert.Single(publisher.ObservedStatesAtPublishTime);
        Assert.Equal(TaskLifecycleState.Planned, observedState);
    }

    [Fact]
    public async Task RollbackAsync_Propagates_AndDoesNotPublish_WhenPersistenceFails()
    {
        var task = NewTask(TaskLifecycleState.Ready);
        // A real DispatchedTaskStore pointed at an unreachable server — forces a genuine
        // persistence failure without inventing a store abstraction/interface purely for
        // testability, matching RetryManagerTests' identical precedent.
        var brokenStore = new DispatchedTaskStore(
            "Server=eos-nonexistent-host-for-tests,1433;Database=master;User Id=sa;Password=invalid;TrustServerCertificate=True;Connect Timeout=1;");
        var publisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(brokenStore, publisher);

        await Assert.ThrowsAsync<SqlException>(() => rollbackManager.RollbackAsync(task, CancellationToken.None));

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task RollbackAsync_LeavesTheTaskAtItsPreviouslyPersistedState_WhenPersistenceFails()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);

        var brokenStore = new DispatchedTaskStore(
            "Server=eos-nonexistent-host-for-tests,1433;Database=master;User Id=sa;Password=invalid;TrustServerCertificate=True;Connect Timeout=1;");
        var rollbackManagerAgainstBrokenStore = new RollbackManager(brokenStore, new RecordingRollbackExecutedEventPublisher());

        await Assert.ThrowsAsync<SqlException>(() => rollbackManagerAgainstBrokenStore.RollbackAsync(task, CancellationToken.None));

        // Re-read through the real, working store: the failed write against brokenStore never
        // touched the actual database, so the Task remains exactly at its last successfully
        // persisted state.
        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persisted!.State);
    }

    [Fact]
    public async Task RollbackAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        var store = await CreateStoreAsync();
        var task = NewTask(TaskLifecycleState.Ready);
        await store.UpsertAsync(task, CancellationToken.None);
        var rollbackManager = new RollbackManager(store, new RecordingRollbackExecutedEventPublisher());
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rollbackManager.RollbackAsync(task, alreadyCancelled.Token));
    }

    // ---------- Multi Task ----------

    [Fact]
    public async Task RollbackAsync_MultiTask_ExecutesInOrder_AndPublishesOneEventPerTask()
    {
        var store = await CreateStoreAsync();
        var t1 = NewTask(TaskLifecycleState.Ready);
        var t2 = NewTask(TaskLifecycleState.Testing);
        var t3 = NewTask(TaskLifecycleState.Verified);
        await store.UpsertAsync(t1, CancellationToken.None);
        await store.UpsertAsync(t2, CancellationToken.None);
        await store.UpsertAsync(t3, CancellationToken.None);
        var publisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(store, publisher);

        var results = await rollbackManager.RollbackAsync([t1, t2, t3], CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Planned, results[0].State);
        Assert.Equal(TaskLifecycleState.Review, results[1].State);
        Assert.Equal(TaskLifecycleState.Testing, results[2].State);
        Assert.Equal(3, publisher.Published.Count);
        Assert.Equal(t1.TaskId, publisher.Published[0].TaskId);
        Assert.Equal(t2.TaskId, publisher.Published[1].TaskId);
        Assert.Equal(t3.TaskId, publisher.Published[2].TaskId);
    }

    [Fact]
    public async Task RollbackAsync_MultiTask_StopsAtTheFailingTask_AndLeavesLaterTasksUntouched()
    {
        var store = await CreateStoreAsync();
        var t1 = NewTask(TaskLifecycleState.Ready);
        var t2 = NewTask(TaskLifecycleState.Blocked); // ambiguous — throws
        var t3 = NewTask(TaskLifecycleState.Verified);
        await store.UpsertAsync(t1, CancellationToken.None);
        await store.UpsertAsync(t2, CancellationToken.None);
        await store.UpsertAsync(t3, CancellationToken.None);
        var publisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(store, publisher);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => rollbackManager.RollbackAsync([t1, t2, t3], CancellationToken.None));

        // T1 already rolled back and its event already published — not compensated/reverted.
        var persistedT1 = await store.GetByIdAsync(t1.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Planned, persistedT1!.State);
        Assert.Single(publisher.Published);
        Assert.Equal(t1.TaskId, publisher.Published[0].TaskId);

        // T2 (the failure) is untouched — the throw happens before any write for T2.
        var persistedT2 = await store.GetByIdAsync(t2.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persistedT2!.State);

        // T3 was never attempted.
        var persistedT3 = await store.GetByIdAsync(t3.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Verified, persistedT3!.State);
    }

    // ---------- RollbackRequested ----------

    [Fact]
    public void OnRollbackRequested_RecordsTheExactPayloadFields()
    {
        var rollbackManager = new RollbackManager(
            new DispatchedTaskStore(TestConnectionString.SqlServer), new RecordingRollbackExecutedEventPublisher());
        var actionId = Guid.NewGuid();

        rollbackManager.OnRollbackRequested(actionId, "Learning Engine", "Demotion criteria retroactively violated.");

        Assert.NotNull(rollbackManager.LastRollbackRequestObserved);
        Assert.Equal(actionId, rollbackManager.LastRollbackRequestObserved.Value.ActionId);
        Assert.Equal("Learning Engine", rollbackManager.LastRollbackRequestObserved.Value.OwningSubsystem);
        Assert.Equal("Demotion criteria retroactively violated.", rollbackManager.LastRollbackRequestObserved.Value.Reason);
    }

    // Documented non-goal, not silently skipped: RollbackRequestedPayload's action_id has no
    // persisted correlation to any DispatchedTask.TaskId anywhere in this codebase (the same,
    // already-disclosed WP-024 limitation as ExecutionCoordinator.DispatchNextAsync's ActionId).
    // "Correct Task is resolved" / "correct rollback path is executed via the event path" are
    // therefore not implementable without either a frozen-contract change to ActionRequest
    // (out of scope) or a new correlation store (forbidden) — see RollbackManager's own doc
    // comment on OnRollbackRequested. This test proves the honest current behavior: observing a
    // request never fabricates a rollback.
    [Fact]
    public void OnRollbackRequested_DoesNotFabricateOrAttemptAnyRollback()
    {
        var publisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(new DispatchedTaskStore(TestConnectionString.SqlServer), publisher);

        rollbackManager.OnRollbackRequested(Guid.NewGuid(), "Memory", "Reason.");

        Assert.Empty(publisher.Published);
    }
}
