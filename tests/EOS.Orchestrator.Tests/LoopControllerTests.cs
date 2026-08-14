using EOS.Contracts;
using Microsoft.Data.SqlClient;

namespace EOS.Orchestrator.Tests;

public class LoopControllerTests
{
    // WP-029: OperationalModeState is a single current-value row (§19.2) with no natural
    // per-test isolation key — reset before every test stack is built, mirroring
    // OperationalModeStoreTests' own DELETE-FROM precedent.
    private static async Task<OperationalModeStore> CreateOperationalModeStoreAsync()
    {
        var store = new OperationalModeStore(TestConnectionString.SqlServer);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        await using var connection = new SqlConnection(TestConnectionString.SqlServer);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OperationalModeState";
        await command.ExecuteNonQueryAsync();

        return store;
    }

    private static async Task<(
        LoopController Controller,
        LoopIterationStore IterationStore,
        RecordingLoopIterationStartedEventPublisher Started,
        RecordingLoopIterationCompletedEventPublisher Completed,
        OperationalModeStore OperationalModeStore,
        RecordingOperationalModeChangedEventPublisher OperationalModeChanged,
        RecordingLoopIterationEvaluatedEventPublisher Evaluated)>
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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var operationalModeChanged = new RecordingOperationalModeChangedEventPublisher();
        var evaluated = new RecordingLoopIterationEvaluatedEventPublisher();

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
            completed,
            operationalModeStore,
            operationalModeChanged,
            evaluated);

        return (controller, iterationStore, started, completed, operationalModeStore, operationalModeChanged, evaluated);
    }

    [Fact]
    public async Task RunIterationAsync_UserRequest_ReachesLoopIterationCompleted_WithFullStepsTraversed()
    {
        var (controller, iterationStore, started, completed, _, _, evaluated) = await CreateStackAsync();

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "add a logging statement"), CancellationToken.None);

        Assert.Single(started.Published);
        Assert.Single(completed.Published);
        var iterationId = started.Published[0].IterationId;
        Assert.Equal(iterationId, completed.Published[0].IterationId);
        Assert.Equal("Completed", completed.Published[0].Outcome);
        // WP-029: steps 16 (Self-Evaluate) + 17 (Improve) now follow the existing 2-15 traversal.
        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17], completed.Published[0].StepsTraversed);

        var persisted = await iterationStore.GetByIdAsync(iterationId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("Completed", persisted!.State);
        Assert.Equal("Completed", persisted.Outcome);
        Assert.Equal(2, persisted.EntryStep);
        Assert.Equal("UserRequest", persisted.TriggerSource);
        Assert.NotNull(persisted.CompletedAt);

        // WP-029: Self-Evaluation published exactly once, with the required null score (Decision 1).
        Assert.Single(evaluated.Published);
        Assert.Equal(iterationId, evaluated.Published[0].IterationId);
        Assert.Null(evaluated.Published[0].LoopHealthScore);
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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var reasoningEngineClient = new FixedReasoningEngineClient(TestDecisions.Low());
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), reasoningEngineClient, new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var stateCapturingCompleted = new StateCapturingLoopIterationCompletedEventPublisher(iterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, new RecordingLoopIterationStartedEventPublisher(), stateCapturingCompleted,
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal(["Completed"], stateCapturingCompleted.ObservedStateAtPublishTime);
    }

    [Fact]
    public async Task RunIterationAsync_Denies_WhenStep7ProtectionValidationDenies_AndNeverReachesStep8()
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, iterationStore, started, completed, _, _, evaluated) = await CreateStackAsync(
            planningClient: recordingPlanningClient, protectionClient: new AlwaysDenyProtectionClient());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal("Denied", completed.Published[0].Outcome);
        // WP-029: Denied is still a legitimate completed iteration (§13.1's "loop_iteration_complete"
        // has no outcome precondition) — Self-Evaluate/Improve (16-17) still run.
        Assert.Equal([2, 3, 4, 5, 6, 7, 16, 17], completed.Published[0].StepsTraversed);

        var persisted = await iterationStore.GetByIdAsync(started.Published[0].IterationId, CancellationToken.None);
        Assert.Equal("Completed", persisted!.State);
        Assert.Equal("Denied", persisted.Outcome);

        // Step 8 (Plan) is genuinely never reached, and Improve (step 17) does not submit a Goal
        // either — sustained decline cannot be detected while loop_health_score is always null
        // (D1/§20.5) — so no Goal of any kind is submitted for a Denied iteration.
        Assert.Empty(recordingPlanningClient.SubmittedGoals);
        Assert.Single(evaluated.Published);
        Assert.Null(evaluated.Published[0].LoopHealthScore);
    }

    [Theory]
    [InlineData(ProtectionVerdict.Defer)]
    [InlineData(ProtectionVerdict.Retry)]
    public async Task RunIterationAsync_Denies_WhenStep7ProtectionValidationIsNotAllow_AndNeverReachesStep8(ProtectionVerdict verdict)
    {
        // Protection Invariant: only Allow may proceed to step 8 — Deny, Defer, and Retry all
        // short-circuit identically.
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, _, completed, _, _, _) = await CreateStackAsync(
            planningClient: recordingPlanningClient, protectionClient: new FixedVerdictProtectionClient(verdict));

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal("Denied", completed.Published[0].Outcome);
        Assert.Equal([2, 3, 4, 5, 6, 7, 16, 17], completed.Published[0].StepsTraversed);
        Assert.DoesNotContain(recordingPlanningClient.SubmittedGoals, goal => goal.Statement == "test goal");
    }

    [Fact]
    public async Task RunIterationAsync_PersistsFailed_AndNeverPublishesCompleted_WhenAStepThrows()
    {
        var (controller, iterationStore, started, completed, _, _, evaluated) = await CreateStackAsync(
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

        // A step failure never reaches Self-Evaluate/Improve — it fails before that point.
        Assert.Empty(evaluated.Published);
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
        var (controller, iterationStore, started, completed, _, _, _) = await CreateStackAsync(
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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new ThrowingReasoningEngineClient(), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnCompleteStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var completed = new RecordingLoopIterationCompletedEventPublisher();
        var startedPublisher = new ThrowingLoopIterationStartedEventPublisher();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, startedPublisher, completed,
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnCompleteStore, new ThrowingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var started = new RecordingLoopIterationStartedEventPublisher();
        var completed = new RecordingLoopIterationCompletedEventPublisher();
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnSuccessStore, started, completed,
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnCompleteStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

        var aggregate = await Assert.ThrowsAsync<AggregateException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal("Simulated terminal-persistence failure.", aggregate.InnerExceptions[0].Message);
        Assert.Equal("Simulated terminal-persistence failure.", aggregate.InnerExceptions[1].Message);
    }

    [Fact]
    public async Task RunIterationAsync_Throws_ForAnUnknownTriggerSource()
    {
        var (controller, _, _, _, _, _, _) = await CreateStackAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => controller.RunIterationAsync(new TriggerContext("GitEvent", null), CancellationToken.None));
    }

    [Theory]
    [InlineData("FileChange")]
    [InlineData("GitEvent")]
    public async Task RunIterationAsync_RejectsFileAndGitTriggers_PermanentlyExcluded(string excludedTriggerSource)
    {
        var (controller, _, _, _, _, _, _) = await CreateStackAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => controller.RunIterationAsync(new TriggerContext(excludedTriggerSource, null), CancellationToken.None));
    }

    [Fact]
    public async Task RunIterationAsync_TwoCallsProduceTwoIndependentIterationIds()
    {
        var (controller, _, started, _, _, _, _) = await CreateStackAsync();

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "goal one"), CancellationToken.None);
        await controller.RunIterationAsync(new TriggerContext("UserRequest", "goal two"), CancellationToken.None);

        Assert.Equal(2, started.Published.Count);
        Assert.NotEqual(started.Published[0].IterationId, started.Published[1].IterationId);
    }

    [Fact]
    public async Task RunIterationAsync_TwoConcurrentCallsProduceTwoIndependentIterationIds()
    {
        var (controller, _, started, _, _, _, _) = await CreateStackAsync();

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
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var stateCapturingStarted = new StateCapturingLoopIterationStartedEventPublisher(iterationStore);
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, stateCapturingStarted, new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal([true], stateCapturingStarted.IterationExistedAtPublishTime);
    }

    [Fact]
    public async Task RunIterationAsync_LearningOpportunity_RecordsSteps13Through17_WithoutSubmittingImproveGoal_WhenSustainedDeclineCannotBeDetected()
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, started, completed, _, _, _) = await CreateStackAsync(planningClient: recordingPlanningClient);

        await controller.RunIterationAsync(new TriggerContext("LearningOpportunity", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(13, started.Published[0].EntryStep);
        // WP-029: Self-Evaluate/Improve (16-17) still traverse for every completed iteration,
        // regardless of entry step — but Improve's Goal submission is gated by sustained-decline
        // detection (§20.5), which can never fire while loop_health_score is always null (D1).
        Assert.Equal([13, 14, 15, 16, 17], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
        Assert.Empty(recordingPlanningClient.SubmittedGoals);
    }

    [Fact]
    public async Task RunIterationAsync_KnowledgeUpdate_RecordsStep15AndImprove()
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, started, completed, _, _, _) = await CreateStackAsync(planningClient: recordingPlanningClient);

        await controller.RunIterationAsync(new TriggerContext("KnowledgeUpdate", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(15, started.Published[0].EntryStep);
        Assert.Equal([15, 16, 17], completed.Published[0].StepsTraversed);
        Assert.Empty(recordingPlanningClient.SubmittedGoals);
    }

    [Fact]
    public async Task RunIterationAsync_PerformanceDegradation_RecordsStep1AndImprove()
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, started, completed, _, _, _) = await CreateStackAsync(planningClient: recordingPlanningClient);

        await controller.RunIterationAsync(new TriggerContext("PerformanceDegradation", "Cpu"), CancellationToken.None);

        Assert.Equal(1, started.Published[0].EntryStep);
        Assert.Equal([1, 16, 17], completed.Published[0].StepsTraversed);
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
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, started, completed, _, _, _) = await CreateStackAsync(planningClient: recordingPlanningClient);

        await controller.RunIterationAsync(new TriggerContext("PerformanceDegradation", unparseablePayload), CancellationToken.None);

        Assert.Equal(1, started.Published[0].EntryStep);
        Assert.Equal([1, 16, 17], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
    }

    [Fact]
    public async Task RunIterationAsync_Failure_RecordsStep11AndImprove()
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, started, completed, _, _, _) = await CreateStackAsync(planningClient: recordingPlanningClient);

        await controller.RunIterationAsync(new TriggerContext("Failure", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(11, started.Published[0].EntryStep);
        Assert.Equal([11, 16, 17], completed.Published[0].StepsTraversed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task RunIterationAsync_Failure_StillRecordsStep11AsTraversed_EvenWhenPayloadDoesNotParse(string? unparseablePayload)
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, started, completed, _, _, _) = await CreateStackAsync(planningClient: recordingPlanningClient);

        await controller.RunIterationAsync(new TriggerContext("Failure", unparseablePayload), CancellationToken.None);

        Assert.Equal(11, started.Published[0].EntryStep);
        Assert.Equal([11, 16, 17], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
    }

    [Fact]
    public async Task RunIterationAsync_ScheduledTask_EntersAtStep9_WithoutResubmittingTheOriginalGoal()
    {
        // §8: "Enters Loop At: Step 8 (Plan) or later, if the task's plan is already known" —
        // realized as entering directly at step 9 (Schedule), never re-submitting a Goal. WP-029:
        // Improve (step 17) does not create another Goal either — sustained decline cannot be
        // detected while loop_health_score is always null (D1/§20.5).
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, started, completed, _, _, _) = await CreateStackAsync(planningClient: recordingPlanningClient);

        await controller.RunIterationAsync(new TriggerContext("ScheduledTask", Guid.NewGuid().ToString()), CancellationToken.None);

        Assert.Equal(8, started.Published[0].EntryStep);
        Assert.Equal([9, 10, 11, 12, 13, 14, 15, 16, 17], completed.Published[0].StepsTraversed);
        Assert.Equal("Completed", completed.Published[0].Outcome);
        Assert.Empty(recordingPlanningClient.SubmittedGoals);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_ReportsAssistedMode_AsTheDefaultWhenNoModeHasEverBeenSet_AndNullLoopHealthScore()
    {
        var (controller, _, _, _, _, _, _) = await CreateStackAsync();

        var status = await controller.GetCurrentStatusAsync(CancellationToken.None);

        // §22.2: Assisted is the Loop's default mode in the absence of an explicit selection —
        // now sourced from OperationalModeStore's own default, not a hard-coded literal (WP-029).
        Assert.Equal(OperationalMode.Assisted, status.CurrentMode);
        Assert.Null(status.LoopHealthScore);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_ReflectsTheMostRecentIteration()
    {
        var (controller, _, started, _, _, _, _) = await CreateStackAsync();
        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        var status = await controller.GetCurrentStatusAsync(CancellationToken.None);

        Assert.Equal(started.Published[^1].IterationId, status.CurrentIterationId);
    }

    // ---- WP-029: Operational Modes ----

    [Fact]
    public async Task SetOperationalModeAsync_Allow_ChangesModePersistsAndPublishesEvent()
    {
        var (controller, _, _, _, operationalModeStore, operationalModeChanged, _) = await CreateStackAsync(
            protectionClient: new AlwaysAllowProtectionClient());

        var result = await controller.SetOperationalModeAsync(OperationalMode.Autonomous, "test-operator", CancellationToken.None);

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Equal(OperationalMode.Autonomous, await operationalModeStore.GetCurrentModeAsync(CancellationToken.None));
        Assert.Single(operationalModeChanged.Published);
        Assert.Equal((OperationalMode.Assisted, OperationalMode.Autonomous, "test-operator"), operationalModeChanged.Published[0]);
    }

    [Fact]
    public async Task SetOperationalModeAsync_RequestedModeEqualsCurrentMode_StillCallsProtection_ButSkipsWriteAndPublish()
    {
        // Claude Code Review finding fix: an idempotent request (mode already active) must not
        // produce a spurious "changed" event describing no actual change, and must not perform an
        // unnecessary persistence write — but Protection is still consulted for every request,
        // idempotent or not; no exemption from Decision-Matrix governance exists for no-ops.
        var recordingProtectionClient = new RecordingActionTypeProtectionClient(ProtectionVerdict.Allow);
        var dispatchedTaskStore = new DispatchedTaskStore(TestConnectionString.SqlServer);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);
        var scheduler = new Scheduler(
            dispatchedTaskStore, new FixedPlanQueryClient(), new FixedGoalPlanQueryClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, recordingProtectionClient, new RecordingTaskStartedEventPublisher());
        var progressMonitor = new ProgressMonitor(dispatchedTaskStore, new FixedGoalPlanQueryClient());
        var iterationStore = new LoopIterationStore(TestConnectionString.SqlServer);
        await iterationStore.EnsureTableExistsAsync(CancellationToken.None);
        var operationalModeStore = new CallCountingOperationalModeStore(await CreateOperationalModeStoreAsync());
        var operationalModeChanged = new RecordingOperationalModeChangedEventPublisher();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), recordingProtectionClient,
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            iterationStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, operationalModeChanged, new RecordingLoopIterationEvaluatedEventPublisher());

        // Default mode is Assisted (§22.2) — request that same mode.
        var result = await controller.SetOperationalModeAsync(OperationalMode.Assisted, "test-operator", CancellationToken.None);

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Single(recordingProtectionClient.ValidatedActions); // Protection was still called.
        Assert.Equal("SetOperationalMode", recordingProtectionClient.ValidatedActions[0].ActionType);
        Assert.Equal(0, operationalModeStore.SetCurrentModeAsyncCallCount); // No unnecessary write.
        Assert.Equal(OperationalMode.Assisted, await operationalModeStore.GetCurrentModeAsync(CancellationToken.None));
        Assert.Empty(operationalModeChanged.Published); // No spurious "changed" event.
    }

    [Fact]
    public async Task SetOperationalModeAsync_Deny_LeavesModeUnchanged_AndDoesNotPublish()
    {
        var (controller, _, _, _, operationalModeStore, operationalModeChanged, _) = await CreateStackAsync(
            protectionClient: new AlwaysDenyProtectionClient());

        var result = await controller.SetOperationalModeAsync(OperationalMode.Autonomous, "test-operator", CancellationToken.None);

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        // The Loop never manufactures Allow itself — Protection's own verdict is the sole authority.
        Assert.Equal(OperationalMode.Assisted, await operationalModeStore.GetCurrentModeAsync(CancellationToken.None));
        Assert.Empty(operationalModeChanged.Published);
    }

    [Theory]
    [InlineData(ProtectionVerdict.Defer)]
    [InlineData(ProtectionVerdict.Retry)]
    public async Task SetOperationalModeAsync_NonAllowVerdicts_AllLeaveModeUnchanged(ProtectionVerdict verdict)
    {
        var (controller, _, _, _, operationalModeStore, operationalModeChanged, _) = await CreateStackAsync(
            protectionClient: new FixedVerdictProtectionClient(verdict));

        var result = await controller.SetOperationalModeAsync(OperationalMode.Safe, "test-operator", CancellationToken.None);

        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(OperationalMode.Assisted, await operationalModeStore.GetCurrentModeAsync(CancellationToken.None));
        Assert.Empty(operationalModeChanged.Published);
    }

    [Fact]
    public async Task SetOperationalModeAsync_SendsTheModeChangeThroughProtectionValidate_AsTheSoleApprovalAuthority()
    {
        // Structural proof the Loop never self-approves (WP-029 Decision 4): the only way this
        // test's ValidationResult can be Allow is if the stub itself returned it — LoopController
        // has no code path that constructs an Allow result on its own.
        var recordingProtectionClient = new RecordingActionTypeProtectionClient(ProtectionVerdict.Allow);
        var (controller, _, _, _, _, _, _) = await CreateStackAsync(protectionClient: recordingProtectionClient);

        await controller.SetOperationalModeAsync(OperationalMode.Manual, "test-operator", CancellationToken.None);

        Assert.Single(recordingProtectionClient.ValidatedActions);
        Assert.Equal("SetOperationalMode", recordingProtectionClient.ValidatedActions[0].ActionType);
        Assert.Equal("test-operator", recordingProtectionClient.ValidatedActions[0].Actor);
    }

    [Fact]
    public async Task SetOperationalModeAsync_ConcurrentCalls_LeaveTheStoreInADeterministicSingleValuedState()
    {
        var (controller, _, _, _, operationalModeStore, _, _) = await CreateStackAsync(
            protectionClient: new AlwaysAllowProtectionClient());

        await Task.WhenAll(
            controller.SetOperationalModeAsync(OperationalMode.Autonomous, "operator-one", CancellationToken.None),
            controller.SetOperationalModeAsync(OperationalMode.Safe, "operator-two", CancellationToken.None));

        var finalMode = await operationalModeStore.GetCurrentModeAsync(CancellationToken.None);
        Assert.True(finalMode is OperationalMode.Autonomous or OperationalMode.Safe);
    }

    [Fact]
    public async Task SetOperationalModeAsync_ConcurrentCalls_PublishOperationalModeChangedWithAuthoritativeFromMode()
    {
        // CodeRabbit pre-merge P1 finding #2: proves the *published event's* from_mode is
        // authoritative under genuine concurrency, not just the final store state (already
        // covered above). Non-brittle: regardless of which call's atomic write the database
        // actually serializes first, exactly one published event must show fromMode == Assisted
        // (the true initial default) and the other must show fromMode == the first event's toMode
        // — this chaining is only possible if from_mode is sourced from the same atomic write as
        // the persistence itself (IOperationalModeStore.SetCurrentModeAsync), never a separately
        // re-read value.
        var (controller, _, _, _, operationalModeStore, operationalModeChanged, _) = await CreateStackAsync(
            protectionClient: new AlwaysAllowProtectionClient());

        await Task.WhenAll(
            controller.SetOperationalModeAsync(OperationalMode.Autonomous, "operator-one", CancellationToken.None),
            controller.SetOperationalModeAsync(OperationalMode.Safe, "operator-two", CancellationToken.None));

        Assert.Equal(2, operationalModeChanged.Published.Count);
        var firstIndex = operationalModeChanged.Published.FindIndex(e => e.FromMode == OperationalMode.Assisted);
        Assert.True(firstIndex >= 0, "Exactly one published event must show the true initial default mode as its fromMode.");
        var first = operationalModeChanged.Published[firstIndex];
        var second = operationalModeChanged.Published[1 - firstIndex];
        Assert.Equal(first.ToMode, second.FromMode);

        var finalMode = await operationalModeStore.GetCurrentModeAsync(CancellationToken.None);
        Assert.Equal(second.ToMode, finalMode);
    }

    [Fact]
    public async Task SetOperationalModeAsync_UsesARiskScoreThatReachesHighTier_AgainstTheRealRiskEngineThresholds()
    {
        // CodeRabbit pre-merge P1 finding #1. Limitation, disclosed rather than worked around:
        // EOS.Orchestrator.Tests has no project reference to EOS.Gates (mirroring
        // EOS.Orchestrator's own frozen dependency shape), so instantiating the real
        // ProtectionGate/RiskEngine in-process here would require a .csproj change, which is
        // outside this fix's authorized scope. This test instead proves the exact RiskScore value
        // SetOperationalModeAsync sends, cross-referenced in the assertion message below against
        // RiskEngine.cs's own real, frozen thresholds (LowTierMaxRiskScore = 30,
        // MediumTierMaxRiskScore = 70): any score in (70, 100] resolves to RiskTier.High, and
        // ProtectionGate.ValidateHighTier is the only tier that invokes ApprovalEngine.Resolve
        // (the Decision Matrix). End-to-end verification against the live ProtectionGate is
        // exercised by EOS.Runner.Tests, which does wire the real composition root — not
        // duplicated here to avoid inventing a second real-pipeline test harness for the same
        // underlying capability.
        var recordingProtectionClient = new RecordingActionTypeProtectionClient(ProtectionVerdict.Allow);
        var (controller, _, _, _, _, _, _) = await CreateStackAsync(protectionClient: recordingProtectionClient);

        await controller.SetOperationalModeAsync(OperationalMode.Autonomous, "test-operator", CancellationToken.None);

        var sentAction = Assert.Single(recordingProtectionClient.ValidatedActions);
        Assert.Equal("SetOperationalMode", sentAction.ActionType);
        Assert.True(
            sentAction.RiskScore > 70 && sentAction.RiskScore <= 100,
            "SetOperationalMode's RiskScore must be high enough to force High-tier Decision Matrix " +
            "routing against the real RiskEngine (§22.9) — see RiskEngine.cs's ClassifyTier thresholds.");
    }

    // ---- WP-029: Emergency Stop ----

    [Fact]
    public async Task EmergencyStopAsync_DelegatesToProtection_WithEmergencyShutdownActionType()
    {
        var recordingProtectionClient = new RecordingActionTypeProtectionClient(ProtectionVerdict.Allow);
        var (controller, _, _, _, _, _, _) = await CreateStackAsync(protectionClient: recordingProtectionClient);

        var result = await controller.EmergencyStopAsync("test-operator", "test reason", CancellationToken.None);

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Single(recordingProtectionClient.ValidatedActions);
        Assert.Equal("EmergencyShutdown", recordingProtectionClient.ValidatedActions[0].ActionType);
        Assert.Equal("test-operator", recordingProtectionClient.ValidatedActions[0].Actor);
    }

    [Fact]
    public async Task EmergencyStopAsync_ReturnsProtectionsVerdictUnchanged_WhenProtectionDefers()
    {
        var (controller, _, _, _, _, _, _) = await CreateStackAsync(protectionClient: new FixedVerdictProtectionClient(ProtectionVerdict.Defer));

        var result = await controller.EmergencyStopAsync("test-operator", "test reason", CancellationToken.None);

        Assert.Equal(ProtectionVerdict.Defer, result.Verdict);
    }

    // ---- WP-029: Self-Evaluation / Improve ----

    [Fact]
    public async Task RunIterationAsync_Improve_NeverSubmitsAGoal_WhenSustainedDeclineCannotBeDetected_ForADeniedDecision()
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, _, _, _, _, _) = await CreateStackAsync(
            planningClient: recordingPlanningClient, protectionClient: new AlwaysDenyProtectionClient());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        // §20.5: submit_goal is gated by "alt sustained decline detected" — with loop_health_score
        // always null (D1), no decline can ever be detected, so Improve never submits a Goal,
        // regardless of the Decision's own Protection verdict.
        Assert.Empty(recordingPlanningClient.SubmittedGoals);
    }

    [Fact]
    public async Task RunIterationAsync_Improve_NeverSubmitsAGoal_WhenSustainedDeclineCannotBeDetected_ForAnAllowedDecision()
    {
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var recordingPlanningClient = new RecordingPlanningClient(plan);
        var (controller, _, _, _, _, _, _) = await CreateStackAsync(
            planningClient: recordingPlanningClient, protectionClient: new AlwaysAllowProtectionClient());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        // Same as above, for the "Completed" (Allowed) outcome — the trigger-derived Goal at step 8
        // is still submitted (via FixedPlanningClient's role covered elsewhere; here it's the same
        // RecordingPlanningClient, so it captures both step 8's Goal and any Improve Goal). Only
        // Improve's own submission is asserted absent here, by checking no Goal carries Improve's
        // distinguishing actor.
        Assert.DoesNotContain(recordingPlanningClient.SubmittedGoals, goal => goal.SubmittedByActor == "AutonomousEngineeringLoop");
    }

    [Fact]
    public async Task RunIterationAsync_WritesEvaluatingThenImproving_BeforeReachingTerminalCompletedState()
    {
        // Final-adversarial-review finding: §19.1 names Evaluating/Improving as the states between
        // Learning and Completed (WP-028 Decision 4 reserved these exact string values for
        // WP-029) — previously only stepsTraversed recorded 16/17, with no corresponding State
        // transition ever written.
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
        var recordingStateStore = new RecordingStateTransitionsLoopIterationStore(realIterationStore);
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            recordingStateStore, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), new RecordingLoopIterationEvaluatedEventPublisher());

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        // Deciding, Executing (existing WP-028 writes), then Evaluating, Improving (WP-029).
        Assert.Equal(["Deciding", "Executing", "Evaluating", "Improving"], recordingStateStore.ObservedStates);
    }

    [Fact]
    public async Task RunIterationAsync_PublishesLoopIterationEvaluated_OnlyAfterImprovingIsPersisted()
    {
        // Claude Code Review finding fix: LoopIterationEvaluated must be published only after
        // BOTH Evaluating and Improving have been persisted and Improve's own logic has run —
        // never before "Improving", mirroring LoopIterationCompleted's own persist-before-publish
        // placement. Uses one shared ordered list across both the state-write calls and the
        // event publish to prove genuine ordering, not merely that both eventually happened.
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
        var orderingRecorder = new EvaluatedOrderingRecorder(realIterationStore);
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            orderingRecorder, new RecordingLoopIterationStartedEventPublisher(), new RecordingLoopIterationCompletedEventPublisher(),
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), orderingRecorder);

        await controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None);

        Assert.Equal(["Deciding", "Executing", "Evaluating", "Improving", "LoopIterationEvaluated"], orderingRecorder.ObservedOrder);
    }

    [Fact]
    public async Task RunIterationAsync_DoesNotPublishLoopIterationEvaluated_WhenTheImprovingStateWriteFails()
    {
        // Claude Code Review finding fix: proves LoopIterationEvaluated is genuinely NOT published
        // when the "Improving" write fails — Evaluating (step 16) must already have succeeded for
        // execution to even reach the throwing "Improving" write, but the iteration still ends up
        // Failed via the existing compensating-write pattern, with Evaluated never sent.
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
        var throwingOnImprovingStore = new ThrowingOnUpdateStateLoopIterationStore(realIterationStore, "Improving");
        var operationalModeStore = await CreateOperationalModeStoreAsync();
        var plan = new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null);
        var started = new RecordingLoopIterationStartedEventPublisher();
        var completed = new RecordingLoopIterationCompletedEventPublisher();
        var evaluated = new RecordingLoopIterationEvaluatedEventPublisher();
        var controller = new LoopController(
            new FixedPlanningClient(plan), new FixedReasoningEngineClient(TestDecisions.Low()), new AlwaysAllowProtectionClient(),
            new FixedTierResourceManagementClient(CapacityTier.Safe), scheduler, executionCoordinator, progressMonitor,
            throwingOnImprovingStore, started, completed,
            operationalModeStore, new RecordingOperationalModeChangedEventPublisher(), evaluated);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.RunIterationAsync(new TriggerContext("UserRequest", "test goal"), CancellationToken.None));
        Assert.Equal("Simulated Improving state-write failure.", thrown.Message);

        Assert.Empty(evaluated.Published);
        Assert.Empty(completed.Published);
        var persisted = await realIterationStore.GetByIdAsync(started.Published[0].IterationId, CancellationToken.None);
        Assert.Equal("Failed", persisted!.State);
        Assert.Equal("Failed", persisted.Outcome);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task RunIterationAsync_SelfEvaluation_NeverProducesANonNullScore_AcrossEveryEntryPath()
    {
        var (controller, _, _, _, _, _, evaluated) = await CreateStackAsync(planningClient: new RecordingPlanningClient(new Plan(Guid.NewGuid(), Guid.NewGuid(), [], 1, 1.0, null)));

        await controller.RunIterationAsync(new TriggerContext("KnowledgeUpdate", Guid.NewGuid().ToString()), CancellationToken.None);
        await controller.RunIterationAsync(new TriggerContext("PerformanceDegradation", "Cpu"), CancellationToken.None);

        Assert.Equal(2, evaluated.Published.Count);
        Assert.All(evaluated.Published, entry => Assert.Null(entry.LoopHealthScore));
    }
}

/// <summary>WP-029: records every validated <see cref="ActionRequest"/> — proves which ActionType/Actor the Loop actually sends to Protection.</summary>
internal sealed class RecordingActionTypeProtectionClient(ProtectionVerdict verdict) : IProtectionClient
{
    public List<ActionRequest> ValidatedActions { get; } = [];

    public ValidationResult Validate(ActionRequest action)
    {
        ValidatedActions.Add(action);
        return new ValidationResult(verdict, RiskTier.Low, "Set by test.");
    }
}
