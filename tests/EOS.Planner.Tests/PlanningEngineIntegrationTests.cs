using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.VectorStore;

namespace EOS.Planner.Tests;

/// <summary>
/// Roadmap Demo/Acceptance criterion (WP-023): "A test Goal (\"add a logging statement to
/// module X\") is decomposed into a sensible, schema-valid Task Graph, citing at least one
/// reusable pattern from Knowledge Management." Real infrastructure throughout (SQL Server,
/// ChromaDB), matching this repository's established integration-test convention.
/// </summary>
public class PlanningEngineIntegrationTests
{
    private static readonly RankingWeights DefaultRankingWeights = new(
        VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);

    private static string SqlConnectionString => TestConnectionString.SqlServer;

    private static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");

    [Fact]
    public async Task SubmitGoalAsync_DecomposesTheDemoGoal_IntoASchemaValidTaskGraph_CitingAReusablePattern()
    {
        var knowledgeGraphStore = new KnowledgeGraphStore(SqlConnectionString);
        await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
        var domainTags = new[] { $"wp023-demo-{Guid.NewGuid()}" };

        var patternContent = "Add the logging statement\nVerify the log output";
        await knowledgeGraphStore.UpsertAsync(
            new KnowledgeNode(Guid.NewGuid(), KnowledgeNodeType.Pattern, patternContent, domainTags, [], DateTimeOffset.UtcNow),
            CancellationToken.None);

        var knowledgeClient = new KnowledgeClient(
            knowledgeGraphStore, DefaultRankingWeights, new ChromaVectorStore(ChromaDbEndpoint), new NeverCalledMemorySourceStore());

        var goalStore = new GoalStore(SqlConnectionString);
        await goalStore.EnsureTableExistsAsync(CancellationToken.None);
        var planStore = new PlanStore(SqlConnectionString);
        await planStore.EnsureTableExistsAsync(CancellationToken.None);
        var goalDependencyStore = new GoalDependencyStore(SqlConnectionString);
        await goalDependencyStore.EnsureTableExistsAsync(CancellationToken.None);

        var goalManager = new GoalManager(goalStore, new NoOpGoalCreatedEventPublisher(), new NoOpGoalCancelledEventPublisher());
        var goalValidator = new GoalValidator(new AlwaysAllowProtectionClient(), new NoOpGoalValidatedEventPublisher());
        var taskGraphBuilder = new TaskGraphBuilder(knowledgeClient, new CountingReasoningEngineClient("unused"));
        var dependencyManager = new DependencyManager(goalDependencyStore, goalStore);
        var priorityManager = new PriorityManager();
        var planningEngine = new PlanningEngine(
            goalManager, goalValidator, taskGraphBuilder, dependencyManager, priorityManager, planStore,
            new NoOpTaskCreatedEventPublisher(), new NoOpPlannerGeneratedEventPublisher());

        var goal = new Goal(
            GoalId: Guid.NewGuid(),
            Statement: "add a logging statement to module X",
            ParentGoalId: null,
            DomainTags: domainTags,
            SubmittedByActor: "Product Owner",
            State: GoalLifecycleState.Proposed,
            PlanId: null);

        var plan = await planningEngine.SubmitGoalAsync(goal, CancellationToken.None);

        Assert.NotEmpty(plan.Tasks);
        Assert.Contains(plan.Tasks, task => task.Description == "Add the logging statement");
        Assert.Contains(plan.Tasks, task => task.Description == "Verify the log output");

        // Schema-valid: every DependsOnTaskIds reference resolves to another Task in the same
        // Plan, and the graph is acyclic by construction (each Task may only depend on an
        // earlier-indexed Task, per TaskGraphBuilder's own sequential-chain construction).
        var taskIds = plan.Tasks.Select(task => task.TaskId).ToHashSet();
        Assert.All(plan.Tasks, task => Assert.All(task.DependsOnTaskIds, dependsOnId => Assert.Contains(dependsOnId, taskIds)));

        var goalStatus = await planningEngine.GetGoalStatusAsync(goal.GoalId.ToString(), CancellationToken.None);
        Assert.Equal(GoalLifecycleState.Planned, goalStatus.State);
        Assert.Equal(plan.PlanId, goalStatus.PlanId);
    }

    /// <summary>
    /// Principal Engineer Final Review F6: when decomposition fails (e.g. Knowledge/Reasoning
    /// infrastructure unavailable), the Goal must not be left stuck in <c>Decomposing</c>
    /// indefinitely — <c>PlanningEngine.SubmitGoalAsync</c> cancels it via the already-defined
    /// §11.6/§22.1 "Cancelled (from any state)" transition and rethrows the original failure.
    /// </summary>
    [Fact]
    public async Task SubmitGoalAsync_CancelsTheGoal_WhenDecompositionFails()
    {
        var goalStore = new GoalStore(SqlConnectionString);
        await goalStore.EnsureTableExistsAsync(CancellationToken.None);
        var planStore = new PlanStore(SqlConnectionString);
        await planStore.EnsureTableExistsAsync(CancellationToken.None);
        var goalDependencyStore = new GoalDependencyStore(SqlConnectionString);
        await goalDependencyStore.EnsureTableExistsAsync(CancellationToken.None);

        var goalManager = new GoalManager(goalStore, new NoOpGoalCreatedEventPublisher(), new NoOpGoalCancelledEventPublisher());
        var goalValidator = new GoalValidator(new AlwaysAllowProtectionClient(), new NoOpGoalValidatedEventPublisher());
        var taskGraphBuilder = new TaskGraphBuilder(new ThrowingKnowledgeClient(), new CountingReasoningEngineClient("unused"));
        var dependencyManager = new DependencyManager(goalDependencyStore, goalStore);
        var priorityManager = new PriorityManager();
        var planningEngine = new PlanningEngine(
            goalManager, goalValidator, taskGraphBuilder, dependencyManager, priorityManager, planStore,
            new NoOpTaskCreatedEventPublisher(), new NoOpPlannerGeneratedEventPublisher());

        var goal = new Goal(
            GoalId: Guid.NewGuid(),
            Statement: "add a logging statement to module X",
            ParentGoalId: null,
            DomainTags: [],
            SubmittedByActor: "Product Owner",
            State: GoalLifecycleState.Proposed,
            PlanId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => planningEngine.SubmitGoalAsync(goal, CancellationToken.None));

        var persisted = await goalStore.GetByIdAsync(goal.GoalId, CancellationToken.None);
        Assert.Equal(GoalLifecycleState.Cancelled, persisted!.State);
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
