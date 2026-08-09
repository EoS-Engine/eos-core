using EOS.Contracts;
using EOS.Gates;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Orchestrator;
using EOS.Planner;
using EOS.VectorStore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Runner.Tests;

/// <summary>
/// WP-024 roadmap Demo/Acceptance criterion: "WP-023's test Goal now actually executes its Task
/// Graph, with each dispatch visibly gated (logged) by Protection." Real infrastructure
/// throughout (SQL Server, ChromaDB, a real <see cref="ProtectionGate"/> — not a stub), matching
/// this repository's established integration-test convention and AskCommandIntegrationTests'
/// precedent for constructing a real Protection gate.
/// </summary>
public class SchedulerExecutionCoordinatorAcceptanceTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static string SqlConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");

    [Fact]
    public async Task SubmitGoalAsync_ThenDispatchNextAsync_ExecutesTheTaskGraph_GatedByRealProtection()
    {
        var resourceManagementClient = new AlwaysSafeResourceManagementClient();
        // Real ProtectionGate (not a stub) gates the Ready → Running transition — the roadmap's
        // own "visibly gated (logged) by Protection" acceptance criterion — matching
        // AskCommandIntegrationTests' identical construction.
        var realProtectionGate = new ProtectionGate(
            new PolicyEngine([], [], [], []), new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(),
            new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
            resourceManagementClient,
            NullLogger<ProtectionGate>.Instance);
        var (planningEngine, scheduler, dispatchedTaskStore, executionCoordinator, taskStartedPublisher) =
            await BuildStackAsync(resourceManagementClient, realProtectionGate);

        var domainTags = new[] { $"wp024-demo-{Guid.NewGuid()}" };
        var goal = new Goal(
            GoalId: Guid.NewGuid(),
            Statement: "add a logging statement to module X",
            ParentGoalId: null,
            DomainTags: domainTags,
            SubmittedByActor: "Product Owner",
            State: GoalLifecycleState.Proposed,
            PlanId: null);

        var plan = await planningEngine.SubmitGoalAsync(goal, CancellationToken.None);
        var task = Assert.Single(plan.Tasks);

        var readyTasks = await scheduler.EvaluateReadinessAsync(CancellationToken.None);
        Assert.Contains(readyTasks, readyTask => readyTask.TaskId == task.TaskId);

        var result = await executionCoordinator.DispatchNextAsync(CancellationToken.None);

        Assert.Equal(DispatchOutcome.Dispatched, result.Outcome);
        Assert.Equal(task.TaskId, result.Task!.TaskId);
        Assert.Equal(TaskLifecycleState.Running, result.Task.State);
        Assert.NotNull(result.Task.RunningAt);
        Assert.Contains(task.TaskId, taskStartedPublisher.PublishedTaskIds);

        var persisted = await dispatchedTaskStore.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Running, persisted!.State);
    }

    // Roadmap Test Verification: "an integration test confirming a Task is denied dispatch when
    // Protection returns Deny." Uses a real ProtectionGate — not a stub — driven to a genuine
    // non-Allow verdict via EmergencyShutdownState (Protection-Layer-Specification-v1.0 §12.5/
    // §26.1: while active, every non-control action is held at Defer). ExecutionCoordinator
    // treats any non-Allow verdict (Deny/Defer/Retry) identically as ProtectionDenied (§25.1:
    // "never treats a Defer as an implicit Allow"), so this exercises the real gate's actual
    // holding behavior, not a stub standing in for it.
    [Fact]
    public async Task DispatchNextAsync_LeavesTheTaskReady_WhenRealProtectionDenies()
    {
        var resourceManagementClient = new AlwaysSafeResourceManagementClient();
        var realProtectionGate = new ProtectionGate(
            new PolicyEngine([], [], [], []), new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(),
            new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
            resourceManagementClient,
            NullLogger<ProtectionGate>.Instance);
        var (planningEngine, scheduler, dispatchedTaskStore, executionCoordinator, taskStartedPublisher) =
            await BuildStackAsync(resourceManagementClient, realProtectionGate);

        var domainTags = new[] { $"wp024-demo-{Guid.NewGuid()}" };
        var goal = new Goal(
            GoalId: Guid.NewGuid(),
            Statement: "add a logging statement to module X",
            ParentGoalId: null,
            DomainTags: domainTags,
            SubmittedByActor: "Product Owner",
            State: GoalLifecycleState.Proposed,
            PlanId: null);

        var plan = await planningEngine.SubmitGoalAsync(goal, CancellationToken.None);
        var task = Assert.Single(plan.Tasks);

        await scheduler.EvaluateReadinessAsync(CancellationToken.None);

        // Activates Emergency Shutdown on this same real ProtectionGate instance — goal
        // submission above already completed via GoalValidator's own AlwaysAllowProtectionClient
        // (a separate instance), so this only affects the dispatch-time gate under test.
        realProtectionGate.Validate(new ActionRequest(Guid.NewGuid(), "EmergencyShutdown", "Test", 10));

        var result = await executionCoordinator.DispatchNextAsync(CancellationToken.None);

        Assert.Equal(DispatchOutcome.ProtectionDenied, result.Outcome);
        Assert.Equal(task.TaskId, result.Task!.TaskId);
        Assert.Empty(taskStartedPublisher.PublishedTaskIds);

        var persisted = await dispatchedTaskStore.GetByIdAsync(task.TaskId, CancellationToken.None);
        Assert.Equal(TaskLifecycleState.Ready, persisted!.State);
        Assert.Null(persisted.RunningAt);
    }

    private static async Task<(
        PlanningEngine PlanningEngine,
        Scheduler Scheduler,
        DispatchedTaskStore DispatchedTaskStore,
        ExecutionCoordinator ExecutionCoordinator,
        RecordingTaskStartedEventPublisher TaskStartedPublisher)> BuildStackAsync(
        IResourceManagementClient resourceManagementClient, IProtectionClient taskDispatchProtectionClient)
    {
        var knowledgeGraphStore = new KnowledgeGraphStore(SqlConnectionString);
        await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
        var knowledgeClient = new KnowledgeClient(
            knowledgeGraphStore, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), new NeverCalledMemorySourceStore());

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
        // Goal Validation (§11.5) is not what either test exercises — always Allow here, so
        // SubmitGoalAsync reaches decomposition/dispatch regardless of which test is running.
        var goalValidator = new GoalValidator(new AlwaysAllowProtectionClient(), new NoOpGoalValidatedEventPublisher());
        var taskGraphBuilder = new TaskGraphBuilder(knowledgeClient, new NeverCalledReasoningEngineClient());
        var dependencyManager = new DependencyManager(goalDependencyStore, goalStore);
        var priorityManager = new PriorityManager();

        // A very high ceiling/capacity — this shared real SQL Server table has no delete/cleanup
        // method (matching every other real-infra store in this codebase) and accumulates
        // Running rows across every prior run of this suite and of EOS.Orchestrator.Tests (same
        // physical table); a small ceiling would eventually, spuriously trip on that history.
        var scheduler = new Scheduler(
            dispatchedTaskStore, new PlanStorePlanQueryClient(planStore), resourceManagementClient,
            concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);

        var planningEngine = new PlanningEngine(
            goalManager, goalValidator, taskGraphBuilder, dependencyManager, priorityManager, planStore,
            new SchedulerTaskCreatedEventPublisher(scheduler),
            new SchedulerPlannerGeneratedEventPublisher(scheduler));

        var taskStartedPublisher = new RecordingTaskStartedEventPublisher();
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, taskDispatchProtectionClient, taskStartedPublisher);

        return (planningEngine, scheduler, dispatchedTaskStore, executionCoordinator, taskStartedPublisher);
    }

    // The DispatchedTask table has no delete/cleanup method (matching every other real-infra
    // store in this codebase), and both Scheduler.SelectNextDispatchableTaskAsync and this
    // table are shared with EOS.Orchestrator.Tests (same physical SQL Server table) — clearing
    // leftover Ready rows via the store's own existing UpsertAsync (no new store method) keeps
    // "the dispatched Task is this test's own" deterministic.
    private static async Task ClearReadyQueueAsync(DispatchedTaskStore store)
    {
        var ready = await store.GetByStateAsync(TaskLifecycleState.Ready, CancellationToken.None);
        foreach (var task in ready)
        {
            await store.UpsertAsync(task with { State = TaskLifecycleState.Cancelled }, CancellationToken.None);
        }
    }

    private sealed class AlwaysAllowProtectionClient : IProtectionClient
    {
        public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Allow, RiskTier.Low, null);
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

    private sealed class PlanStorePlanQueryClient(PlanStore planStore) : IPlanQueryClient
    {
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
            planStore.GetByIdAsync(planId, cancellationToken);
    }

    private sealed class SchedulerTaskCreatedEventPublisher(Scheduler scheduler) : ITaskCreatedEventPublisher
    {
        public void PublishTaskCreated(Guid taskId, string[] competenciesRequired, int priority) =>
            scheduler.OnTaskCreated(taskId, priority);
    }

    private sealed class SchedulerPlannerGeneratedEventPublisher(Scheduler scheduler) : IPlannerGeneratedEventPublisher
    {
        public void PublishPlannerGenerated(Guid planId, Guid taskGraphRef) =>
            scheduler.OnPlannerGeneratedAsync(planId, CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed class RecordingTaskStartedEventPublisher : ITaskStartedEventPublisher
    {
        public List<Guid> PublishedTaskIds { get; } = [];

        public void PublishTaskStarted(Guid taskId) => PublishedTaskIds.Add(taskId);
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

    private sealed class NeverCalledReasoningEngineClient : IReasoningEngineClient
    {
        public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Should not be called — no reusable pattern matches this test's unique domain tag.");

        public Task<ConfidenceGuardResult> CompareAsync(
            PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class NeverCalledMemorySourceStore : IMemorySourceStore
    {
        public Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }
}
