using EOS.Contracts;
using EOS.Gates;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Orchestrator;
using EOS.Planner;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Runner.Tests;

/// <summary>
/// WP-025.8: the roadmap's own end-to-end acceptance criterion — "A deliberately-failing test
/// Task correctly retries, then rolls back, then triggers Recovery Planning and resumes" /
/// "...ending in either a successful resumed execution or a clean, explained failure." Real
/// infrastructure throughout (SQL Server, a real <see cref="ProtectionGate"/>), matching
/// <see cref="SchedulerExecutionCoordinatorAcceptanceTests"/>'s established convention. Uses a
/// fixed (non-ChromaDB) Knowledge client, since Task Graph decomposition content is not what this
/// test verifies.
///
/// The Constitution §6.2 Rollback Path for <c>Running → Blocked</c> is genuinely two-valued
/// ("→ Running (after fix) or → Cancelled") with no derivable discriminator — WP-025.3's Board-
/// accepted <see cref="RollbackManager"/> correctly refuses (<see cref="NotSupportedException"/>)
/// rather than guessing. This test therefore demonstrates the roadmap's own explicitly-anticipated
/// "clean, explained failure" outcome for that one step, then demonstrates the independent §16.1
/// "Task permanently Blocked" replanning trigger succeeding regardless (§16.1 names this trigger
/// separately from, not contingent on, a prior successful rollback).
/// </summary>
public class RetryRollbackReplanAcceptanceTests
{
    private static string SqlConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private const int RetryMaxAttempts = 2;

    [Fact]
    public async Task DeliberatelyFailingTask_RetriesThenExhausts_RollbackHonestlyRefuses_ThenReplanSucceeds()
    {
        var goalStore = new GoalStore(SqlConnectionString);
        await goalStore.EnsureTableExistsAsync(CancellationToken.None);
        var planStore = new PlanStore(SqlConnectionString);
        await planStore.EnsureTableExistsAsync(CancellationToken.None);
        var goalDependencyStore = new GoalDependencyStore(SqlConnectionString);
        await goalDependencyStore.EnsureTableExistsAsync(CancellationToken.None);
        var dispatchedTaskStore = new DispatchedTaskStore(SqlConnectionString);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        await ClearReadyQueueAsync(dispatchedTaskStore);

        var goalManager = new GoalManager(goalStore, new NoOpGoalCreatedEventPublisher(), new NoOpGoalCancelledEventPublisher());
        var realProtectionGate = new ProtectionGate(
            new PolicyEngine([], [], [], []), new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(),
            new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
            new AlwaysSafeResourceManagementClient(),
            NullLogger<ProtectionGate>.Instance);
        var goalValidator = new GoalValidator(realProtectionGate, new NoOpGoalValidatedEventPublisher());
        var taskGraphBuilder = new TaskGraphBuilder(new FixedKnowledgeClient(), new NeverCalledReasoningEngineClient());
        var dependencyManager = new DependencyManager(goalDependencyStore, goalStore);
        var priorityManager = new PriorityManager();
        var replanPublisher = new RecordingReplanTriggeredEventPublisher();
        var planningEngine = new PlanningEngine(
            goalManager, goalValidator, taskGraphBuilder, dependencyManager, priorityManager, planStore,
            new NoOpTaskCreatedEventPublisher(), new NoOpPlannerGeneratedEventPublisher(), replanPublisher);
        var replanRequestClient = new PlanningEngineReplanRequestClient(planningEngine);

        var goalPlanQueryClient = new GoalStoreGoalPlanQueryClient(goalStore);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new PlanStorePlanQueryClient(planStore), goalPlanQueryClient,
            new AlwaysSafeResourceManagementClient(), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, realProtectionGate, new RecordingTaskStartedEventPublisher());
        var taskRetriedPublisher = new RecordingTaskRetriedEventPublisher();
        var retryManager = new RetryManager(
            dispatchedTaskStore, realProtectionGate, taskRetriedPublisher,
            retryMaxAttemptsCount: RetryMaxAttempts, retryBackoffDelaySeconds: 0, retryTimeoutSeconds: 300);
        var rollbackExecutedPublisher = new RecordingRollbackExecutedEventPublisher();
        var rollbackManager = new RollbackManager(dispatchedTaskStore, rollbackExecutedPublisher);

        // 1. Submit a Goal — real decomposition (fixed pattern), real Protection validation.
        var domainTags = new[] { $"wp025-8-acceptance-{Guid.NewGuid()}" };
        var goal = new Goal(
            GoalId: Guid.NewGuid(), Statement: "deliberately-failing acceptance task", ParentGoalId: null,
            DomainTags: domainTags, SubmittedByActor: "Product Owner", State: GoalLifecycleState.Proposed, PlanId: null);
        var oldPlan = await planningEngine.SubmitGoalAsync(goal, CancellationToken.None);
        var oldPlanTask = Assert.Single(oldPlan.Tasks);

        // 2. Materialize + Ready + Dispatch — the deliberately-failing Task reaches Running.
        scheduler.OnTaskCreated(oldPlanTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(oldPlan.PlanId, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var dispatchResult = await executionCoordinator.DispatchNextAsync(CancellationToken.None);
        Assert.Equal(DispatchOutcome.Dispatched, dispatchResult.Outcome);
        Assert.Equal(oldPlanTask.TaskId, dispatchResult.Task!.TaskId);

        // 3. Deliberate failure injection (roadmap's own "deliberately-failing test Task" —
        // consistent with WP-024's own precedent that no real work-execution component exists).
        var runningTask = (await dispatchedTaskStore.GetByIdAsync(oldPlanTask.TaskId, CancellationToken.None))!;
        var blockedTask = runningTask with { State = TaskLifecycleState.Blocked, BlockedReason = "Deliberately injected acceptance failure." };
        await dispatchedTaskStore.UpsertAsync(blockedTask, CancellationToken.None);

        // 4. Retry until the budget is exhausted — real Protection re-validated on every attempt.
        var current = blockedTask;
        for (var attempt = 0; attempt < RetryMaxAttempts; attempt++)
        {
            current = await retryManager.RetryAsync(current, CancellationToken.None);
            Assert.Equal(TaskLifecycleState.Running, current.State);
            // Fail again immediately (deliberately-failing task), so the next iteration retries.
            current = current with { State = TaskLifecycleState.Blocked, BlockedReason = "Deliberately injected acceptance failure." };
            await dispatchedTaskStore.UpsertAsync(current, CancellationToken.None);
        }

        var exhausted = await retryManager.RetryAsync(current, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, exhausted.State);
        Assert.Equal(RetryMaxAttempts, exhausted.RetryCount);
        Assert.Equal(RetryMaxAttempts, taskRetriedPublisher.Published.Count);

        // 5. Rollback of the now-permanently-Blocked Task: honestly refused, not guessed — the
        // roadmap's own explicitly-anticipated "clean, explained failure" outcome.
        var rollbackFailure = await Assert.ThrowsAsync<NotSupportedException>(
            () => rollbackManager.RollbackAsync(exhausted, CancellationToken.None));
        Assert.Contains("Running → Blocked Rollback Path is", rollbackFailure.Message);
        Assert.Empty(rollbackExecutedPublisher.Published);
        var stillBlocked = await dispatchedTaskStore.GetByIdAsync(exhausted.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, stillBlocked!.State);

        // 6. §16.1's independently-named "Task permanently Blocked" trigger — replanning
        // proceeds regardless of the rollback outcome above (not contingent on it).
        var revisedPlan = await replanRequestClient.RequestReplanAfterFailureAsync(goal.GoalId, CancellationToken.None);
        Assert.Equal(oldPlan.PlanId, revisedPlan.PreviousPlanId);
        var newPlanTask = Assert.Single(revisedPlan.Tasks);
        var single = Assert.Single(replanPublisher.Published);
        Assert.Equal(goal.GoalId, single.GoalId);
        Assert.Equal("Failure", single.TriggerType);

        // 7. Old-Plan Task (permanently Blocked, exhausted) remains persisted, exactly as it was
        // — never cancelled, never mutated, and now structurally non-dispatchable because its
        // PlanId no longer matches Goal.PlanId.
        var persistedOldPlanTask = await dispatchedTaskStore.GetByIdAsync(oldPlanTask.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Blocked, persistedOldPlanTask!.State);
        Assert.Equal(oldPlan.PlanId, persistedOldPlanTask.PlanId);
        Assert.Equal(RetryMaxAttempts, persistedOldPlanTask.RetryCount);

        // 8. New-Plan Task materializes and IS dispatchable — a successful resumed execution.
        scheduler.OnTaskCreated(newPlanTask.TaskId, priority: 1);
        await scheduler.OnPlannerGeneratedAsync(revisedPlan.PlanId, CancellationToken.None);
        await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        var resumedDispatch = await executionCoordinator.DispatchNextAsync(CancellationToken.None);

        Assert.Equal(DispatchOutcome.Dispatched, resumedDispatch.Outcome);
        Assert.Equal(newPlanTask.TaskId, resumedDispatch.Task!.TaskId);
        Assert.Equal(TaskLifecycleState.Running, resumedDispatch.Task.State);
    }

    // The DispatchedTask table has no delete/cleanup method (matching every other real-infra
    // store in this codebase), and is shared with EOS.Orchestrator.Tests/
    // SchedulerExecutionCoordinatorAcceptanceTests (same physical SQL Server table) — clearing
    // leftover Ready rows via the store's own existing UpsertAsync (no new store method) keeps
    // "the dispatched Task is this test's own" deterministic, mirroring
    // SchedulerExecutionCoordinatorAcceptanceTests' own identical precedent.
    private static async Task ClearReadyQueueAsync(DispatchedTaskStore store)
    {
        var ready = await store.GetByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);
        foreach (var task in ready)
        {
            await store.UpsertAsync(task with { State = TaskLifecycleState.Cancelled }, CancellationToken.None);
        }
    }

    private sealed class AlwaysSafeResourceManagementClient : IResourceManagementClient
    {
        public double GetCurrentBudget(ResourceType resourceType) => 0;

        public CapacityTier GetCurrentTier(ResourceType resourceType) => CapacityTier.Safe;

        public ModelResidencyStatus GetModelResidency(string modelId) => new(modelId, ModelResidencyState.Unloaded, null);

        public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass)
        {
        }
    }

    private sealed class FixedKnowledgeClient : IKnowledgeClient
    {
        public Task UpdateAsync(
            Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
            KnowledgeMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<IEnumerable<KnowledgeNode>> QueryAsync(
            MemoryType? type, string[]? domainTags, DateRange? range, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<KnowledgeNode>>([]);

        public Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<Guid> ConsolidateAsync(
            MemoryRef source, string reason, string[] evidenceRefs, bool suppressLessonLearned = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class NeverCalledReasoningEngineClient : IReasoningEngineClient
    {
        public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<ConfidenceGuardResult> CompareAsync(
            PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class PlanStorePlanQueryClient(PlanStore planStore) : IPlanQueryClient
    {
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
            planStore.GetByIdAsync(planId, cancellationToken);
    }

    private sealed class GoalStoreGoalPlanQueryClient(GoalStore goalStore) : IGoalPlanQueryClient
    {
        public async Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default) =>
            (await goalStore.GetByIdAsync(goalId, cancellationToken))?.PlanId;
    }

    private sealed class PlanningEngineReplanRequestClient(PlanningEngine planningEngine) : IReplanRequestClient
    {
        public Task<Plan> RequestReplanAfterFailureAsync(Guid goalId, CancellationToken cancellationToken = default) =>
            planningEngine.ReplanAfterFailureAsync(goalId, cancellationToken);
    }

    private sealed class RecordingTaskStartedEventPublisher : ITaskStartedEventPublisher
    {
        public List<Guid> PublishedTaskIds { get; } = [];

        public void PublishTaskStarted(Guid taskId) => PublishedTaskIds.Add(taskId);
    }

    private sealed class RecordingTaskRetriedEventPublisher : ITaskRetriedEventPublisher
    {
        public List<(Guid TaskId, int AttemptNumber)> Published { get; } = [];

        public void PublishTaskRetried(Guid taskId, int attemptNumber) => Published.Add((taskId, attemptNumber));
    }

    private sealed class RecordingRollbackExecutedEventPublisher : IRollbackExecutedEventPublisher
    {
        public List<(Guid TaskId, string RollbackPathUsed)> Published { get; } = [];

        public void PublishRollbackExecuted(Guid taskId, string rollbackPathUsed) => Published.Add((taskId, rollbackPathUsed));
    }

    private sealed class RecordingReplanTriggeredEventPublisher : IReplanTriggeredEventPublisher
    {
        public List<(Guid GoalId, string TriggerType)> Published { get; } = [];

        public void PublishReplanTriggered(Guid goalId, string triggerType) => Published.Add((goalId, triggerType));
    }

    private sealed class NoOpGoalCreatedEventPublisher : IGoalCreatedEventPublisher
    {
        public void PublishGoalCreated(Guid goalId, Guid? parentGoalId, string statement)
        {
        }
    }

    private sealed class NoOpGoalCancelledEventPublisher : IGoalCancelledEventPublisher
    {
        public void PublishGoalCancelled(Guid goalId, string reason)
        {
        }
    }

    private sealed class NoOpGoalValidatedEventPublisher : IGoalValidatedEventPublisher
    {
        public void PublishGoalValidated(Guid goalId, bool feasibilityResult)
        {
        }
    }

    private sealed class NoOpTaskCreatedEventPublisher : ITaskCreatedEventPublisher
    {
        public void PublishTaskCreated(Guid taskId, string[] competenciesRequired, int priority)
        {
        }
    }

    private sealed class NoOpPlannerGeneratedEventPublisher : IPlannerGeneratedEventPublisher
    {
        public void PublishPlannerGenerated(Guid planId, Guid taskGraphRef)
        {
        }
    }
}
