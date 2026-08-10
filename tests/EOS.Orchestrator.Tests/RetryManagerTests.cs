using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Orchestrator.Tests;

public class RetryManagerTests
{
    private const int MaxAttempts = 3;
    private const int BackoffSeconds = 30;
    private const int TimeoutSeconds = 300;

    private static async Task<DispatchedTaskStore> CreateStoreAsync()
    {
        var store = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        return store;
    }

    private static RetryManager NewRetryManager(DispatchedTaskStore store, IProtectionClient protectionClient, ITaskRetriedEventPublisher publisher) =>
        new(store, protectionClient, publisher, MaxAttempts, BackoffSeconds, TimeoutSeconds);

    // RunningAt defaults to safely past the backoff window so tests that aren't specifically
    // about backoff timing don't need to reason about it.
    private static DispatchedTask NewBlockedTask(int retryCount = 0, DateTimeOffset? runningAt = null) => new(
        TaskId: Guid.NewGuid(),
        PlanId: Guid.NewGuid(),
        GoalId: Guid.NewGuid(),
        Description: "Retryable task",
        CompetencyRequirements: ["logging"],
        DependsOnTaskIds: [],
        Priority: 1,
        State: TaskLifecycleState.Blocked,
        SchedulingMode: SchedulingMode.Immediate,
        NotBefore: null,
        RunningAt: runningAt ?? DateTimeOffset.UtcNow.AddSeconds(-(BackoffSeconds + 1)),
        EventObserved: false,
        RetryCount: retryCount,
        BlockedReason: "Gate failure: coverage below threshold");

    [Fact]
    public async Task RetryAsync_TransitionsToRunning_IncrementsRetryCount_AndPublishesTaskRetried_WhenEligible()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: 1);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), publisher);

        var result = await retryManager.RetryAsync(task, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Running, result.State);
        Assert.Equal(2, result.RetryCount);
        Assert.Equal(task.TaskId, result.TaskId);
        Assert.Contains((task.TaskId, 2), publisher.Published);

        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Running, persisted!.State);
        Assert.Equal(2, persisted.RetryCount);
    }

    // TaskRetried's payload shape (task_id, attempt_number only) is enforced at compile time by
    // ITaskRetriedEventPublisher's own signature — this proves the two values passed are exactly
    // the original TaskId and the post-increment RetryCount, nothing fabricated or substituted.
    [Fact]
    public async Task RetryAsync_PublishesTheExactTaskIdAndPostIncrementAttemptNumber()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: 0);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), publisher);

        await retryManager.RetryAsync(task, CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(task.TaskId, published.TaskId);
        Assert.Equal(1, published.AttemptNumber);
    }

    [Fact]
    public async Task RetryAsync_PublishesTaskRetried_OnlyAfterTheRetryTransitionIsAlreadyPersisted()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: 0);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new StateCapturingTaskRetriedEventPublisher(store);
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), publisher);

        await retryManager.RetryAsync(task, CancellationToken.None);

        var observedState = Assert.Single(publisher.ObservedStatesAtPublishTime);
        Assert.Equal(TaskLifecycleState.Running, observedState);
    }

    [Fact]
    public async Task RetryAsync_LeavesTaskBlocked_AndDoesNotPublish_WhenRetryBudgetIsExhausted()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: MaxAttempts);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), publisher);

        var result = await retryManager.RetryAsync(task, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Blocked, result.State);
        Assert.Equal(MaxAttempts, result.RetryCount);
        Assert.Empty(publisher.Published);

        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Equal(MaxAttempts, persisted.RetryCount);
    }

    [Fact]
    public async Task RetryAsync_PreservesTheSameTaskId_WhenRetrySucceeds()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: 0);
        await store.UpsertAsync(task, CancellationToken.None);
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), new RecordingTaskRetriedEventPublisher());

        var result = await retryManager.RetryAsync(task, CancellationToken.None);

        Assert.Equal(task.TaskId, result.TaskId);
        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(TaskLifecycleState.Running, persisted.State);
    }

    [Fact]
    public async Task RetryAsync_LeavesTaskBlocked_AndDoesNotPublish_WhenProtectionDenies()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: 0);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(store, new AlwaysDenyProtectionClient(), publisher);

        var result = await retryManager.RetryAsync(task, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Blocked, result.State);
        Assert.Equal(0, result.RetryCount);
        Assert.Empty(publisher.Published);

        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Equal(0, persisted.RetryCount);
    }

    [Fact]
    public async Task RetryAsync_Propagates_AndDoesNotPublish_WhenProtectionThrows()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: 0);
        await store.UpsertAsync(task, CancellationToken.None);
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(store, new ThrowingProtectionClient(), publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => retryManager.RetryAsync(task, CancellationToken.None));

        Assert.Empty(publisher.Published);
        var persisted = await store.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persisted!.State);
        Assert.Equal(0, persisted.RetryCount);
    }

    [Fact]
    public async Task RetryAsync_Propagates_AndDoesNotPublish_WhenPersistenceFails()
    {
        var task = NewBlockedTask(retryCount: 0);
        // A real DispatchedTaskStore pointed at an unreachable server — forces a genuine
        // persistence failure without inventing a store abstraction/interface purely for
        // testability (this codebase's stores are concrete SQL-backed classes throughout).
        var brokenStore = new DispatchedTaskStore(
            "Server=eos-nonexistent-host-for-tests,1433;Database=master;User Id=sa;Password=invalid;TrustServerCertificate=True;Connect Timeout=1;");
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(brokenStore, new AlwaysAllowProtectionClient(), publisher);

        await Assert.ThrowsAsync<SqlException>(() => retryManager.RetryAsync(task, CancellationToken.None));

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task RetryAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        var store = await CreateStoreAsync();
        var task = NewBlockedTask(retryCount: 0);
        await store.UpsertAsync(task, CancellationToken.None);
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), new RecordingTaskRetriedEventPublisher());
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => retryManager.RetryAsync(task, alreadyCancelled.Token));
    }

    // §13.8: timeout is evaluated through this exact same method, never a second mechanism — a
    // Running Task past RetryTimeoutSeconds is retried exactly like a Blocked one.
    [Fact]
    public async Task RetryAsync_EntersTheSameRetryRuleEvaluationPath_WhenARunningTaskHasTimedOut()
    {
        var store = await CreateStoreAsync();
        var timedOut = new DispatchedTask(
            TaskId: Guid.NewGuid(), PlanId: Guid.NewGuid(), GoalId: Guid.NewGuid(),
            Description: "Timed-out task", CompetencyRequirements: ["logging"], DependsOnTaskIds: [],
            Priority: 1, State: TaskLifecycleState.Running, SchedulingMode: SchedulingMode.Immediate,
            NotBefore: null, RunningAt: DateTimeOffset.UtcNow.AddSeconds(-(TimeoutSeconds + 1)),
            EventObserved: false, RetryCount: 0, BlockedReason: null);
        await store.UpsertAsync(timedOut, CancellationToken.None);
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), publisher);

        var result = await retryManager.RetryAsync(timedOut, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Running, result.State);
        Assert.Equal(1, result.RetryCount);
        Assert.Contains((timedOut.TaskId, 1), publisher.Published);
    }

    [Fact]
    public async Task RetryAsync_IsNotEligible_BeforeBackoffElapses_AndBecomesEligible_Afterward()
    {
        var store = await CreateStoreAsync();
        var recentlyRunning = NewBlockedTask(retryCount: 0, runningAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        await store.UpsertAsync(recentlyRunning, CancellationToken.None);
        var publisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = NewRetryManager(store, new AlwaysAllowProtectionClient(), publisher);

        var tooSoon = await retryManager.RetryAsync(recentlyRunning, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Blocked, tooSoon.State);
        Assert.Equal(0, tooSoon.RetryCount);
        Assert.Empty(publisher.Published);

        var elapsed = recentlyRunning with { RunningAt = DateTimeOffset.UtcNow.AddSeconds(-(BackoffSeconds + 1)) };
        await store.UpsertAsync(elapsed, CancellationToken.None);

        var afterBackoff = await retryManager.RetryAsync(elapsed, CancellationToken.None);

        Assert.Equal(TaskLifecycleState.Running, afterBackoff.State);
        Assert.Equal(1, afterBackoff.RetryCount);
        Assert.Contains((elapsed.TaskId, 1), publisher.Published);
    }
}
