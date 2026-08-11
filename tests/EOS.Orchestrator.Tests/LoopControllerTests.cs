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
    public async Task RunIterationAsync_Step3_SuppliesANonNullReasoningContextScope_SoContextAssemblyActuallyRuns()
    {
        // Final targeted fix: Step 3 (Retrieve Context) previously recorded as traversed without
        // ever actually supplying ReasoningContextScope, so ReasoningEngine's own Context
        // Assembly (ProcessContextAsync) never ran for any WP-028-initiated request — this proves
        // the real ReasoningRequest the Loop sends now carries a non-null ContextScope.
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
        var reasoningEngineClient = new FixedReasoningEngineClient(TestDecisions.Low());
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), reasoningEngineClient, new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.NotNull(reasoningEngineClient.LastRequest);
        Assert.NotNull(reasoningEngineClient.LastRequest!.ContextScope);
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

    [Theory]
    [InlineData(ProtectionVerdict.Defer)]
    [InlineData(ProtectionVerdict.Retry)]
    public async Task RunIterationAsync_Denies_WhenStep7ProtectionValidationIsNotAllow_AndNeverReachesStep8(ProtectionVerdict verdict)
    {
        // Protection Invariant: only Allow may proceed to step 8 — Deny, Defer, and Retry all
        // short-circuit identically.
        var (controller, _, _, completed) = await CreateStackAsync(
            planningClient: new NeverCalledPlanningClient(), protectionClient: new FixedVerdictProtectionClient(verdict));

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal("Denied", completed.Published[0].Outcome);
        Assert.Equal([2, 3, 4, 5, 6, 7], completed.Published[0].StepsTraversed);
    }

    [Fact]
    public async Task RunIterationAsync_PersistsFailed_AndNeverPublishesCompleted_WhenAStepThrows()
    {
        var (controller, iterationStore, started, completed) = await CreateStackAsync(
            reasoningEngineClient: new ThrowingReasoningEngineClient());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));
        Assert.Equal("Simulated Reasoning Engine failure.", thrown.Message);

        Assert.Single(started.Published);
        Assert.Empty(completed.Published);

        // CodeRabbit R1 finding #2: the fix makes State/Outcome/CompletedAt persistable together
        // for a failed terminal iteration — previously Outcome/CompletedAt could never be set.
        var persisted = await iterationStore.GetByIdAsync(started.Published[0].IterationId, CancellationToken.None);
        Assert.Equal("Failed", persisted!.State);
        Assert.Equal("Failed", persisted.Outcome);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task RunIterationAsync_PersistsFailed_WhenTheOriginalFailureIsACancellation()
    {
        // CodeRabbit R1 finding #3 / independent pre-review HIGH finding: previously the
        // compensating Failed-state write reused the same (already-cancelled) token as the
        // original failure, so it would itself throw before ever persisting Failed. The fix uses
        // CancellationToken.None for the compensating write specifically to prevent this. The
        // token starts uncancelled (so InsertAsync/Started succeed normally) and is cancelled
        // mid-iteration by the failing step itself, matching a real mid-flight cancellation.
        using var cts = new CancellationTokenSource();
        var (controller, iterationStore, started, completed) = await CreateStackAsync(
            reasoningEngineClient: new CancellingReasoningEngineClient(cts));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), cts.Token));

        Assert.Single(started.Published);
        Assert.Empty(completed.Published);
        var persisted = await iterationStore.GetByIdAsync(started.Published[0].IterationId, CancellationToken.None);
        Assert.Equal("Failed", persisted!.State);
        Assert.Equal("Failed", persisted.Outcome);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task RunIterationAsync_PreservesBothExceptions_WhenTerminalPersistenceItselfFails()
    {
        // CodeRabbit R1 finding #3: a failure in the compensating write must never silently
        // replace the original exception — both must be observable via AggregateException.
        var dispatchedTaskStore = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());
        var progressMonitor = new ProgressMonitor(dispatchedTaskStore, new FixedGoalPlanQueryClient());
        var realIterationStore = new LoopIterationStore(TestConnectionString.SqlServer);
        await realIterationStore.EnsureTableExistsAsync(CancellationToken.None);
        var throwingOnCompleteStore = new ThrowingOnCompleteLoopIterationStore(realIterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new ThrowingReasoningEngineClient(), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnCompleteStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher());

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.IsType<InvalidOperationException>(aggregate.InnerExceptions[0]);
        Assert.Equal("Simulated Reasoning Engine failure.", aggregate.InnerExceptions[0].Message);
        Assert.Equal("Simulated terminal-persistence failure.", aggregate.InnerExceptions[1].Message);
    }

    [Fact]
    public async Task RunIterationAsync_PersistsFailed_WhenLoopIterationStartedPublicationFails()
    {
        // Independent pre-review finding (post-R1): previously a LoopIterationStarted publish
        // failure left the row stuck at "Triggered" forever with no compensating write at all.
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
        var completed = new RecordingLoopIterationCompletedEventPublisher();
        var startedPublisher = new ThrowingLoopIterationStartedEventPublisher();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, startedPublisher, completed);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));
        Assert.Equal("Simulated LoopIterationStarted publication failure.", thrown.Message);

        Assert.Empty(completed.Published);
        var persisted = await iterationStore.GetByIdAsync(startedPublisher.LastIterationId!.Value, CancellationToken.None);
        Assert.Equal("Failed", persisted!.State);
        Assert.Equal("Failed", persisted.Outcome);
        Assert.Empty(persisted.StepsTraversed);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task RunIterationAsync_PreservesBothExceptions_WhenLoopIterationStartedPublicationAndFailurePersistenceBothFail()
    {
        var dispatchedTaskStore = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());
        var progressMonitor = new ProgressMonitor(dispatchedTaskStore, new FixedGoalPlanQueryClient());
        var realIterationStore = new LoopIterationStore(TestConnectionString.SqlServer);
        await realIterationStore.EnsureTableExistsAsync(CancellationToken.None);
        var throwingOnCompleteStore = new ThrowingOnCompleteLoopIterationStore(realIterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnCompleteStore, new ThrowingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher());

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal("Simulated LoopIterationStarted publication failure.", aggregate.InnerExceptions[0].Message);
        Assert.Equal("Simulated terminal-persistence failure.", aggregate.InnerExceptions[1].Message);
    }

    [Fact]
    public async Task RunIterationAsync_PersistsFailed_WhenSuccessfulTerminalPersistenceFails()
    {
        // Independent pre-review finding (post-R1): previously the successful CompleteAsync call
        // was unprotected — a failure there left the row stuck at its last non-terminal state.
        var dispatchedTaskStore = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());
        var progressMonitor = new ProgressMonitor(dispatchedTaskStore, new FixedGoalPlanQueryClient());
        var realIterationStore = new LoopIterationStore(TestConnectionString.SqlServer);
        await realIterationStore.EnsureTableExistsAsync(CancellationToken.None);
        var throwingOnSuccessStore = new ThrowingOnSuccessfulCompleteLoopIterationStore(realIterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var started = new RecordingLoopIterationStartedEventPublisher();
        var completed = new RecordingLoopIterationCompletedEventPublisher();
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnSuccessStore, started, completed);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));
        Assert.Equal("Simulated successful-terminal-persistence failure.", thrown.Message);

        Assert.Empty(completed.Published);
        var persisted = await realIterationStore.GetByIdAsync(started.Published[0].IterationId, CancellationToken.None);
        Assert.Equal("Failed", persisted!.State);
        Assert.Equal("Failed", persisted.Outcome);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task RunIterationAsync_PreservesBothExceptions_WhenSuccessfulTerminalPersistenceAndFailurePersistenceBothFail()
    {
        var dispatchedTaskStore = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, new AlwaysAllowProtectionClient(), new RecordingTaskStartedEventPublisher());
        var progressMonitor = new ProgressMonitor(dispatchedTaskStore, new FixedGoalPlanQueryClient());
        var realIterationStore = new LoopIterationStore(TestConnectionString.SqlServer);
        await realIterationStore.EnsureTableExistsAsync(CancellationToken.None);
        // Always throws on CompleteAsync regardless of state — both the successful write and the
        // compensating Failed write fail.
        var throwingOnCompleteStore = new ThrowingOnCompleteLoopIterationStore(realIterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnCompleteStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher());

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal("Simulated terminal-persistence failure.", aggregate.InnerExceptions[0].Message);
        Assert.Equal("Simulated terminal-persistence failure.", aggregate.InnerExceptions[1].Message);
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
    public async Task RunIterationAsync_TwoConcurrentCallsProduceTwoIndependentIterationIds()
    {
        var (controller, _, started, _) = await CreateStackAsync();

        await Task.WhenAll(
            controller.RunIterationAsync(new TriggerContext("UserRequest", "concurrent goal one"), CancellationToken.None),
            controller.RunIterationAsync(new TriggerContext("UserRequest", "concurrent goal two"), CancellationToken.None));

        Assert.Equal(2, started.Published.Count);
        Assert.NotEqual(started.Published[0].IterationId, started.Published[1].IterationId);
    }

    [Fact]
    public async Task RunIterationAsync_PublishesLoopIterationStarted_OnlyAfterPersistingTheTriggeredRow()
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
        var stateCapturingStarted = new StateCapturingLoopIterationStartedEventPublisher(iterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, stateCapturingStarted, new RecordingLoopIterationCompletedEventPublisher());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal([true], stateCapturingStarted.IterationExistedAtPublishTime);
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

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-resource-type")]
    public async Task RunIterationAsync_PerformanceDegradation_StillRecordsStep1AsTraversed_EvenWhenPayloadDoesNotParse(
        string? unparseablePayload)
    {
        // Independent pre-review MEDIUM finding: step 1 is recorded as traversed regardless of
        // whether the conditional GetCurrentTier citation actually ran — documented here rather
        // than silently left untested.
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("PerformanceDegradation", unparseablePayload), CancellationToken.None);

        Assert.Equal(1, started.Published[0].EntryStep);
        Assert.Equal([1], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
    }

    [Fact]
    public async Task RunIterationAsync_Failure_RecordsOnlyStep11()
    {
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("Failure", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(11, started.Published[0].EntryStep);
        Assert.Equal([11], completed.Published[0].StepsTraversed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task RunIterationAsync_Failure_StillRecordsStep11AsTraversed_EvenWhenPayloadDoesNotParse(string? unparseablePayload)
    {
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("Failure", unparseablePayload), CancellationToken.None);

        Assert.Equal(11, started.Published[0].EntryStep);
        Assert.Equal([11], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
    }

    [Fact]
    public async Task RunIterationAsync_ScheduledTask_EntersAtStep9_WithoutSubmittingAGoal()
    {
        // §8: "Enters Loop At: Step 8 (Plan) or later, if the task's plan is already known" —
        // realized as entering directly at step 9 (Schedule), never re-submitting a Goal.
        var (controller, _, started, completed) = await CreateStackAsync(planningClient: new NeverCalledPlanningClient());

        await controller.RunIterationAsync(new TriggerContext("ScheduledTask", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(8, started.Published[0].EntryStep);
        Assert.Equal([9, 10, 11, 12, 13, 14, 15], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
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
