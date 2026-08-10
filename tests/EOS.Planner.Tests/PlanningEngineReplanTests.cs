using EOS.Contracts;
using EOS.Knowledge;

namespace EOS.Planner.Tests;

/// <summary>
/// WP-025.7: Planning-Execution-Engine-Specification-v1.0 §16.1 "Replanning After Failures" —
/// real infrastructure throughout (SQL Server), matching this repository's established
/// integration-test convention (<see cref="PlanningEngineIntegrationTests"/>).
/// </summary>
public class PlanningEngineReplanTests
{
    private static string SqlConnectionString => TestConnectionString.SqlServer;

    private static async Task<(GoalStore GoalStore, PlanStore PlanStore, PlanningEngine PlanningEngine, RecordingReplanTriggeredEventPublisher ReplanPublisher)>
        BuildStackAsync(IProtectionClient protectionClient, IKnowledgeClient? knowledgeClient = null)
    {
        var goalStore = new GoalStore(SqlConnectionString);
        await goalStore.EnsureTableExistsAsync(CancellationToken.None);
        var planStore = new PlanStore(SqlConnectionString);
        await planStore.EnsureTableExistsAsync(CancellationToken.None);
        var goalDependencyStore = new GoalDependencyStore(SqlConnectionString);
        await goalDependencyStore.EnsureTableExistsAsync(CancellationToken.None);

        var goalManager = new GoalManager(goalStore, new NoOpGoalCreatedEventPublisher(), new NoOpGoalCancelledEventPublisher());
        var goalValidator = new GoalValidator(protectionClient, new NoOpGoalValidatedEventPublisher());
        var taskGraphBuilder = new TaskGraphBuilder(
            knowledgeClient ?? new FixedKnowledgeClient([]), new CountingReasoningEngineClient("unused"));
        var dependencyManager = new DependencyManager(goalDependencyStore, goalStore);
        var priorityManager = new PriorityManager();
        var replanPublisher = new RecordingReplanTriggeredEventPublisher();
        var planningEngine = new PlanningEngine(
            goalManager, goalValidator, taskGraphBuilder, dependencyManager, priorityManager, planStore,
            new NoOpTaskCreatedEventPublisher(), new NoOpPlannerGeneratedEventPublisher(), replanPublisher);

        return (goalStore, planStore, planningEngine, replanPublisher);
    }

    private static async Task<Goal> SeedSubmittedGoalAsync(GoalStore goalStore, PlanningEngine planningEngine)
    {
        var goal = new Goal(
            GoalId: Guid.NewGuid(),
            Statement: "add a logging statement to module X",
            ParentGoalId: null,
            DomainTags: [],
            SubmittedByActor: "Product Owner",
            State: GoalLifecycleState.Proposed,
            PlanId: null);

        await planningEngine.SubmitGoalAsync(goal, CancellationToken.None);
        return (await goalStore.GetByIdAsync(goal.GoalId, CancellationToken.None))!;
    }

    [Fact]
    public async Task ReplanAfterFailureAsync_ProducesANewPlan_ReferencingThePreviousPlan_AndMovesGoalPlanId()
    {
        var (goalStore, planStore, planningEngine, replanPublisher) = await BuildStackAsync(new AlwaysAllowProtectionClient());
        var submittedGoal = await SeedSubmittedGoalAsync(goalStore, planningEngine);
        var originalPlanId = submittedGoal.PlanId!.Value;

        var revisedPlan = await planningEngine.ReplanAfterFailureAsync(submittedGoal.GoalId, CancellationToken.None);

        Assert.NotEqual(originalPlanId, revisedPlan.PlanId);
        Assert.Equal(originalPlanId, revisedPlan.PreviousPlanId);
        Assert.Equal(submittedGoal.GoalId, revisedPlan.GoalId);

        var persistedGoal = await goalStore.GetByIdAsync(submittedGoal.GoalId, CancellationToken.None);
        Assert.Equal(revisedPlan.PlanId, persistedGoal!.PlanId);

        var persistedRevisedPlan = await planStore.GetByIdAsync(revisedPlan.PlanId, CancellationToken.None);
        Assert.NotNull(persistedRevisedPlan);
        Assert.Equal(originalPlanId, persistedRevisedPlan.PreviousPlanId);

        var single = Assert.Single(replanPublisher.Published);
        Assert.Equal(submittedGoal.GoalId, single.GoalId);
        Assert.Equal("Failure", single.TriggerType);
    }

    [Fact]
    public async Task ReplanAfterFailureAsync_LeavesTheOldPlanPersisted_ExactlyAsItWas()
    {
        var (goalStore, planStore, planningEngine, _) = await BuildStackAsync(new AlwaysAllowProtectionClient());
        var submittedGoal = await SeedSubmittedGoalAsync(goalStore, planningEngine);
        var originalPlanId = submittedGoal.PlanId!.Value;
        var originalPlan = await planStore.GetByIdAsync(originalPlanId, CancellationToken.None);

        await planningEngine.ReplanAfterFailureAsync(submittedGoal.GoalId, CancellationToken.None);

        var stillPersistedOriginalPlan = await planStore.GetByIdAsync(originalPlanId, CancellationToken.None);
        Assert.NotNull(stillPersistedOriginalPlan);
        Assert.Equal(originalPlan!.PlanId, stillPersistedOriginalPlan.PlanId);
        Assert.Equal(originalPlan.Tasks.Length, stillPersistedOriginalPlan.Tasks.Length);
        Assert.Null(stillPersistedOriginalPlan.PreviousPlanId);
    }

    [Fact]
    public async Task ReplanAfterFailureAsync_Throws_AndLeavesGoalPlanIdUnchanged_WhenReValidationDenies()
    {
        var (goalStore, _, submitPlanningEngine, _) = await BuildStackAsync(new AlwaysAllowProtectionClient());
        var submittedGoal = await SeedSubmittedGoalAsync(goalStore, submitPlanningEngine);
        var originalPlanId = submittedGoal.PlanId!.Value;

        // A separate PlanningEngine instance, sharing the same real store, whose Protection
        // client now denies — simulates re-validation failing specifically at replan time.
        var (_, _, denyingPlanningEngine, replanPublisher) = await BuildStackAsync(new AlwaysDenyProtectionClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => denyingPlanningEngine.ReplanAfterFailureAsync(submittedGoal.GoalId, CancellationToken.None));

        Assert.Empty(replanPublisher.Published);
        var persistedGoal = await goalStore.GetByIdAsync(submittedGoal.GoalId, CancellationToken.None);
        Assert.Equal(originalPlanId, persistedGoal!.PlanId);
    }

    [Fact]
    public async Task ReplanAfterFailureAsync_Throws_AndLeavesGoalPlanIdUnchanged_WhenDecompositionFails()
    {
        var (goalStore, _, submitPlanningEngine, _) = await BuildStackAsync(new AlwaysAllowProtectionClient());
        var submittedGoal = await SeedSubmittedGoalAsync(goalStore, submitPlanningEngine);
        var originalPlanId = submittedGoal.PlanId!.Value;

        var (_, _, failingPlanningEngine, replanPublisher) = await BuildStackAsync(
            new AlwaysAllowProtectionClient(), new ThrowingKnowledgeClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingPlanningEngine.ReplanAfterFailureAsync(submittedGoal.GoalId, CancellationToken.None));

        // Deliberately different from SubmitGoalAsync's own failure behavior: the Goal is NOT
        // cancelled — it already has a valid, unaffected current Plan.
        Assert.Empty(replanPublisher.Published);
        var persistedGoal = await goalStore.GetByIdAsync(submittedGoal.GoalId, CancellationToken.None);
        Assert.Equal(GoalLifecycleState.Planned, persistedGoal!.State);
        Assert.Equal(originalPlanId, persistedGoal.PlanId);
    }

    [Fact]
    public async Task ReplanAfterFailureAsync_Throws_WhenTheGoalDoesNotExist()
    {
        var (_, _, planningEngine, replanPublisher) = await BuildStackAsync(new AlwaysAllowProtectionClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => planningEngine.ReplanAfterFailureAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Empty(replanPublisher.Published);
    }

    [Fact]
    public async Task ReplanAfterFailureAsync_PropagatesCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        var (goalStore, _, planningEngine, _) = await BuildStackAsync(new AlwaysAllowProtectionClient());
        var submittedGoal = await SeedSubmittedGoalAsync(goalStore, planningEngine);
        using var alreadyCancelled = new CancellationTokenSource();
        await alreadyCancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => planningEngine.ReplanAfterFailureAsync(submittedGoal.GoalId, alreadyCancelled.Token));
    }

    // P1 closure-audit fix: Constitution Part 6 §6.2/§11.6's "Any → Cancelled" is terminal — no
    // frozen document defines a path back out. Without a guard, AttachPlanAsync's unconditional
    // State = Planned write would silently resurrect a Cancelled Goal.
    [Fact]
    public async Task ReplanAfterFailureAsync_Rejects_ACancelledGoal_WithoutAnySideEffect()
    {
        var (goalStore, planStore, submitPlanningEngine, _) = await BuildStackAsync(new AlwaysAllowProtectionClient());
        var submittedGoal = await SeedSubmittedGoalAsync(goalStore, submitPlanningEngine);
        var originalPlanId = submittedGoal.PlanId!.Value;
        var originalPlan = await planStore.GetByIdAsync(originalPlanId, CancellationToken.None);
        await goalStore.UpsertAsync(submittedGoal with { State = GoalLifecycleState.Cancelled }, CancellationToken.None);

        // ThrowingKnowledgeClient: if the guard were ever bypassed, decomposition would run and
        // this test would fail with a different, decomposition-caused exception instead —
        // structurally proving decomposition never executes, not merely asserting it didn't.
        var (_, _, replanPlanningEngine, replanPublisher) = await BuildStackAsync(
            new AlwaysAllowProtectionClient(), new ThrowingKnowledgeClient());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => replanPlanningEngine.ReplanAfterFailureAsync(submittedGoal.GoalId, CancellationToken.None));

        // Asserting the message distinguishes the guard firing from a decomposition failure —
        // ThrowingKnowledgeClient also throws InvalidOperationException, so the type alone would
        // not prove the guard (not decomposition) is what actually rejected this call.
        Assert.Contains("cannot be replanned", thrown.Message);
        Assert.DoesNotContain("Knowledge infrastructure", thrown.Message);

        // No new Plan persisted, Goal.PlanId/State unchanged, no ReplanTriggered published, no
        // existing Plan/Task modified.
        Assert.Empty(replanPublisher.Published);
        var persistedGoal = await goalStore.GetByIdAsync(submittedGoal.GoalId, CancellationToken.None);
        Assert.Equal(GoalLifecycleState.Cancelled, persistedGoal!.State);
        Assert.Equal(originalPlanId, persistedGoal.PlanId);
        var persistedOriginalPlan = await planStore.GetByIdAsync(originalPlanId, CancellationToken.None);
        Assert.Equal(originalPlan!.Tasks.Length, persistedOriginalPlan!.Tasks.Length);
        Assert.Null(persistedOriginalPlan.PreviousPlanId);
    }

    // P1 closure-audit fix: same terminal-state reasoning for Completed. GoalLifecycleState.Completed
    // has no producing method anywhere in this codebase yet (Execution Coordinator's future scope,
    // per GoalLifecycleState's own doc comment) — constructed directly via the store, matching this
    // test suite's existing precedent for exercising states no current code path yet reaches.
    [Fact]
    public async Task ReplanAfterFailureAsync_Rejects_ACompletedGoal_WithoutAnySideEffect()
    {
        var (goalStore, _, submitPlanningEngine, _) = await BuildStackAsync(new AlwaysAllowProtectionClient());
        var submittedGoal = await SeedSubmittedGoalAsync(goalStore, submitPlanningEngine);
        var originalPlanId = submittedGoal.PlanId!.Value;
        await goalStore.UpsertAsync(submittedGoal with { State = GoalLifecycleState.Completed }, CancellationToken.None);

        var (_, _, replanPlanningEngine, replanPublisher) = await BuildStackAsync(
            new AlwaysAllowProtectionClient(), new ThrowingKnowledgeClient());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => replanPlanningEngine.ReplanAfterFailureAsync(submittedGoal.GoalId, CancellationToken.None));

        Assert.Contains("cannot be replanned", thrown.Message);
        Assert.DoesNotContain("Knowledge infrastructure", thrown.Message);
        Assert.Empty(replanPublisher.Published);
        var persistedGoal = await goalStore.GetByIdAsync(submittedGoal.GoalId, CancellationToken.None);
        Assert.Equal(GoalLifecycleState.Completed, persistedGoal!.State);
        Assert.Equal(originalPlanId, persistedGoal.PlanId);
    }
}
