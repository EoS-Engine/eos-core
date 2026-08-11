using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

public class LoopControllerTests
{
    private static async Task<(
        LoopController Controller,
        LoopIterationStore IterationStore,
        RecordingLoopIterationStartedEventPublisher Started,
        RecordingLoopIterationCompletedEventPublisher Completed)>
        CreateStackAsync(
            IPlanningClient? planningClient = null,
            IReasoningEngineClient? reasoningEngineClient = null,
            IProtectionClient? protectionClient = null)
    {
        var dispatchedTaskStore = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, protectionClient ?? new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());
        var progressMonitor = new ProgressMonitor(dispatchedTaskStore, new FixedGoalPlanQueryClient());

        var iterationStore = new LoopIterationStore(TestConnectionString.SqlServer);
        await iterationStore.EnsureTableExistsAsync(CancellationToken.None);
        var started = new RecordingLoopIterationStartedEventPublisher();
        var completed = new RecordingLoopIterationCompletedEventPublisher();

        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            planningClient ?? new FixedPlanningClient(plan),
            reasoningEngineClient ?? new FixedReasoningEngineClient(TestDecisions.Low()),
            protectionClient ?? new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe),
            scheduler,
            executionCoordinator,
            progressMonitor,
            iterationStore,
            started,
            completed);

        return (controller, iterationStore, started, completed);
    }

    [Fact]
    public async Task RunIterationAsync_UserRequest_ReachesLoopIterationCompleted_WithFullStepsTraversed()
    {
        var (controller, iterationStore, started, completed) = await CreateStackAsync();

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "add a logging statement"), CancellationToken.None);

        Assert.Single(started.Published);
        Assert.Single(completed.Published);
        var iterationId = started.Published[0].IterationId;
        Assert.Equal(iterationId, completed.Published[0].IterationId);
        Assert.Equal("Completed", completed.Published[0].Outcome);
        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15], completed.Published[0].StepsTraversed);

        var persisted = await iterationStore.GetByIdAsync(iterationId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("Completed", persisted!.State);
        Assert.Equal("Completed", persisted.Outcome);
        Assert.Equal(2, persisted.EntryStep);
        Assert.Equal("UserRequest", persisted.TriggerSource);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task RunIterationAsync_PublishesLoopIterationCompleted_OnlyAfterPersistingCompletedState()
    {
        var dispatchedTaskStore = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());
        var progressMonitor = new ProgressMonitor(dispatchedTaskStore, new FixedGoalPlanQueryClient());
        var iterationStore = new LoopIterationStore(TestConnectionString.SqlServer);
        await iterationStore.EnsureTableExistsAsync(CancellationToken.None);
        var stateCapturingCompleted = new StateCapturingLoopIterationCompletedEventPublisher(iterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, new RecordingLoopIterationStartedEventPublisher(), stateCapturingCompleted);

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal(["Completed"], stateCapturingCompleted.ObservedStateAtPublishTime);
    }

    [Fact]
    public async Task RunIterationAsync_Denies_WhenStep7ProtectionValidationDenies_AndNeverReachesStep8()
    {
        var (controller, iterationStore, started, completed) = await CreateStackAsync(
            planningClient: new NeverCalledPlanningClient(), protectionClient: new AlwaysDenyProtectionClient());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal("Denied", completed.Published[0].Outcome);
        Assert.Equal([2, 3, 4, 5, 6, 7], completed.Published[0].StepsTraversed);

        var persisted = await iterationStore.GetByIdAsync(started.Published[0].IterationId, CancellationToken.None);
        Assert.Equal("Completed", persisted!.State);
        Assert.Equal("Denied", persisted.Outcome);
    }

    [Fact]
    public async Task RunIterationAsync_PersistsFailed_AndNeverPublishesCompleted_WhenAStepThrows()
    {
        var (controller, iterationStore, started, completed) = await CreateStackAsync(
            reasoningEngineClient: new ThrowingReasoningEngineClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));

        Assert.Single(started.Published);
        Assert.Empty(completed.Published);

        var persisted = await iterationStore.GetByIdAsync(started.Published[0].IterationId, CancellationToken.None);
        Assert.Equal("Failed", persisted!.State);
        Assert.Null(persisted.Outcome);
        Assert.Null(persisted.CompletedAt);
    }

    [Fact]
    public async Task RunIterationAsync_Throws_ForAnUnknownTriggerSource()
    {
        var (controller, _, _, _) = await CreateStackAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => controller.RunIterationAsync(new TriggerContext("GitEvent", null), CancellationToken.None));
    }

    [Theory]
    [InlineData("FileChange")]
    [InlineData("GitEvent")]
    public async Task RunIterationAsync_RejectsFileAndGitTriggers_PermanentlyExcluded(string excludedTriggerSource)
    {
        var (controller, _, _, _) = await CreateStackAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => controller.RunIterationAsync(new TriggerContext(excludedTriggerSource, null), CancellationToken.None));
    }

    [Fact]
    public async Task RunIterationAsync_TwoCallsProduceTwoIndependentIterationIds()
    {
        var (controller, _, started, _) = await CreateStackAsync();

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "goal one"), CancellationToken.None);
        await controller.RunIterationAsync(new TriggerContext("UserRequest", "goal two"), CancellationToken.None);

        Assert.Equal(2, started.Published.Count);
        Assert.NotEqual(started.Published[0].IterationId, started.Published[1].IterationId);
    }

    [Fact]
    public async Task RunIterationAsync_LearningOpportunity_RecordsSteps13Through15_WithoutSubmittingAGoal()
    {
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("LearningOpportunity", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(13, started.Published[0].EntryStep);
        Assert.Equal([13, 14, 15], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
    }

    [Fact]
    public async Task RunIterationAsync_KnowledgeUpdate_RecordsOnlyStep15()
    {
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("KnowledgeUpdate", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(15, started.Published[0].EntryStep);
        Assert.Equal([15], completed.Published[0].StepsTraversed);
    }

    [Fact]
    public async Task RunIterationAsync_PerformanceDegradation_RecordsOnlyStep1()
    {
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("PerformanceDegradation", "Cpu"), CancellationToken.None);

        Assert.Equal(1, started.Published[0].EntryStep);
        Assert.Equal([1], completed.Published[0].StepsTraversed);
    }

    [Fact]
    public async Task RunIterationAsync_Failure_RecordsOnlyStep11()
    {
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("Failure", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(11, started.Published[0].EntryStep);
        Assert.Equal([11], completed.Published[0].StepsTraversed);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_AlwaysReportsAssistedMode_AndNullLoopHealthScore()
    {
        var (controller, _, _, _) = await CreateStackAsync();

        var status = await controller.GetCurrentStatusAsync(CancellationToken.None);

        Assert.Equal(OperationalMode.Assisted, status.CurrentMode);
        Assert.Null(status.LoopHealthScore);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_ReflectsTheMostRecentIteration()
    {
        var (controller, _, started, _) = await CreateStackAsync();
        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        var status = await controller.GetCurrentStatusAsync(CancellationToken.None);

        Assert.Equal(started.Published[^1].IterationId, status.CurrentIterationId);
    }
}
