using EOS.Contracts;

namespace EOS.Orchestrator.Tests;

internal static class TestConnectionString
{
    public static string SqlServer =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");
}

/// <summary>
/// Hand-rolled <see cref="IPlanQueryClient"/> stub (this repository uses no mocking framework)
/// returning a fixed set of <see cref="Plan"/> instances keyed by <see cref="Plan.PlanId"/>.
/// </summary>
internal sealed class FixedPlanQueryClient(params Plan[] plans) : IPlanQueryClient
{
    public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken = default) =>
        Task.FromResult(plans.FirstOrDefault(plan => plan.PlanId == planId));
}

/// <summary>
/// Hand-rolled <see cref="IGoalPlanQueryClient"/> stub (this repository uses no mocking
/// framework) returning a fixed, mutable set of (GoalId -> current PlanId) mappings — mutable
/// (via <see cref="SetCurrentPlanId"/>) because several existing test fixtures construct the
/// Scheduler before the Plan/Goal pair under test is known.
/// </summary>
internal sealed class FixedGoalPlanQueryClient : IGoalPlanQueryClient
{
    private readonly Dictionary<Guid, Guid> _currentPlanIdByGoalId;

    public FixedGoalPlanQueryClient(params (Guid GoalId, Guid PlanId)[] currentPlans) =>
        _currentPlanIdByGoalId = currentPlans.ToDictionary(mapping => mapping.GoalId, mapping => mapping.PlanId);

    public FixedGoalPlanQueryClient(Plan plan)
        : this((plan.GoalId, plan.PlanId))
    {
    }

    public void SetCurrentPlanId(Guid goalId, Guid planId) => _currentPlanIdByGoalId[goalId] = planId;

    public Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_currentPlanIdByGoalId.TryGetValue(goalId, out var planId) ? (Guid?)planId : null);
}

/// <summary>
/// Hand-rolled <see cref="IResourceManagementClient"/> stub whose tier is configurable per test —
/// used to exercise <c>Scheduler.SelectNextDispatchableTaskAsync</c>'s Resource Budget headroom
/// check (§7.3 step 3).
/// </summary>
internal sealed class FixedTierResourceManagementClient(CapacityTier tier) : IResourceManagementClient
{
    public double GetCurrentBudget(ResourceType resourceType) => 0;

    public CapacityTier GetCurrentTier(ResourceType resourceType) => tier;

    public ModelResidencyStatus GetModelResidency(string modelId) => new(modelId, ModelResidencyState.Unloaded, null);

    public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass)
    {
    }
}

/// <summary>
/// WP-025.6: mirrors <see cref="FixedTierResourceManagementClient"/> exactly, except
/// <see cref="Tier"/> is settable — mutable via a plain property, matching
/// <see cref="FixedGoalPlanQueryClient"/>'s own mutable-test-double precedent (WP-025.3/.4).
/// Used to prove <c>Scheduler.SelectNextDispatchableTaskAsync</c> reads resource state fresh on
/// every call rather than caching an eligibility result — no production event/callback
/// mechanism is introduced or implied.
/// </summary>
internal sealed class MutableTierResourceManagementClient(CapacityTier tier) : IResourceManagementClient
{
    public CapacityTier Tier { get; set; } = tier;

    public double GetCurrentBudget(ResourceType resourceType) => 0;

    public CapacityTier GetCurrentTier(ResourceType resourceType) => Tier;

    public ModelResidencyStatus GetModelResidency(string modelId) => new(modelId, ModelResidencyState.Unloaded, null);

    public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass)
    {
    }
}

internal sealed class AlwaysAllowProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Allow, RiskTier.Low, null);
}

internal sealed class AlwaysDenyProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Deny, RiskTier.Low, "Denied by test.");
}

/// <summary>Configurable-verdict stub — used to prove Defer/Retry short-circuit identically to Deny (Step 7's Protection Invariant).</summary>
internal sealed class FixedVerdictProtectionClient(ProtectionVerdict verdict) : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) => new(verdict, RiskTier.Low, "Set by test.");
}

/// <summary>
/// Simulates Protection infrastructure genuinely failing (as opposed to returning a Deny
/// verdict) — used to prove <see cref="ExecutionCoordinator"/> never converts a Protection
/// failure into a false-success dispatch.
/// </summary>
internal sealed class ThrowingProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) =>
        throw new InvalidOperationException("Simulated Protection infrastructure failure.");
}

internal sealed class RecordingTaskStartedEventPublisher : ITaskStartedEventPublisher
{
    public List<Guid> PublishedTaskIds { get; } = [];

    public void PublishTaskStarted(Guid taskId) => PublishedTaskIds.Add(taskId);
}

/// <summary>
/// Records the <see cref="DispatchedTask"/> state observed via <paramref name="store"/> at the
/// exact moment each <c>TaskStarted</c> fires — proves the event is published strictly after the
/// Ready → Running persistence write, not before or concurrently with it.
/// </summary>
internal sealed class StateCapturingTaskStartedEventPublisher(DispatchedTaskStore store) : ITaskStartedEventPublisher
{
    public List<TaskLifecycleState> ObservedStatesAtPublishTime { get; } = [];

    public void PublishTaskStarted(Guid taskId) =>
        ObservedStatesAtPublishTime.Add(store.GetByIdAsync(taskId, CancellationToken.None).GetAwaiter().GetResult()!.State);
}

internal sealed class RecordingTaskRetriedEventPublisher : ITaskRetriedEventPublisher
{
    public List<(Guid TaskId, int AttemptNumber)> Published { get; } = [];

    public void PublishTaskRetried(Guid taskId, int attemptNumber) => Published.Add((taskId, attemptNumber));
}

/// <summary>
/// Mirrors <see cref="StateCapturingTaskStartedEventPublisher"/>'s exact precedent — proves
/// <c>TaskRetried</c> is published strictly after the retry transition's persistence write, not
/// before or concurrently with it.
/// </summary>
internal sealed class StateCapturingTaskRetriedEventPublisher(DispatchedTaskStore store) : ITaskRetriedEventPublisher
{
    public List<TaskLifecycleState> ObservedStatesAtPublishTime { get; } = [];

    public void PublishTaskRetried(Guid taskId, int attemptNumber) =>
        ObservedStatesAtPublishTime.Add(store.GetByIdAsync(taskId, CancellationToken.None).GetAwaiter().GetResult()!.State);
}

internal sealed class RecordingRollbackExecutedEventPublisher : IRollbackExecutedEventPublisher
{
    public List<(Guid TaskId, string RollbackPathUsed)> Published { get; } = [];

    public void PublishRollbackExecuted(Guid taskId, string rollbackPathUsed) => Published.Add((taskId, rollbackPathUsed));
}

/// <summary>Hand-rolled <see cref="IPlanningClient"/> stub — returns a fixed <see cref="Plan"/> for any submitted Goal.</summary>
internal sealed class FixedPlanningClient(Plan plan) : IPlanningClient
{
    public Task<Plan> SubmitGoalAsync(Goal goal, CancellationToken cancellationToken = default) => Task.FromResult(plan);

    public Task<GoalStatus> GetGoalStatusAsync(string goalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task CancelGoalAsync(string goalId, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");
}

/// <summary>Hand-rolled <see cref="IReasoningEngineClient"/> stub — only <see cref="ReasonAsync"/> is used by WP-028.</summary>
internal sealed class FixedReasoningEngineClient(Decision decision) : IReasoningEngineClient
{
    public ReasoningRequest? LastRequest { get; private set; }

    public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new[] { decision });
    }

    public Task<ConfidenceGuardResult> CompareAsync(
        PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");
}

/// <summary>Proves step 8 (Plan) is never reached when step 7 (Validate) denies.</summary>
internal sealed class NeverCalledPlanningClient : IPlanningClient
{
    public Task<Plan> SubmitGoalAsync(Goal goal, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("SubmitGoalAsync must not be called when step 7 denies.");

    public Task<GoalStatus> GetGoalStatusAsync(string goalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task CancelGoalAsync(string goalId, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");
}

/// <summary>
/// WP-029: records every submitted <see cref="Goal"/> instead of throwing. Improve (step 17) may
/// submit a Quarterly-review Goal via <c>SubmitGoalAsync</c>, but only when sustained decline is
/// detected — never today, since <c>loop_health_score</c> is always null (Decision 1); this is
/// intentional, specification-compliant behavior (§20.5), not a missing implementation. Used to
/// prove exactly which Goal (if any) was submitted — the trigger-derived one (step 8) versus
/// Improve's own (step 17) — by inspecting <see cref="SubmittedGoals"/>'s <c>Statement</c> and
/// <c>SubmittedByActor</c> values.
/// </summary>
internal sealed class RecordingPlanningClient(Plan plan) : IPlanningClient
{
    public List<Goal> SubmittedGoals { get; } = [];

    public Task<Plan> SubmitGoalAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        SubmittedGoals.Add(goal);
        return Task.FromResult(plan);
    }

    public Task<GoalStatus> GetGoalStatusAsync(string goalId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028/WP-029's LoopController.");

    public Task CancelGoalAsync(string goalId, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028/WP-029's LoopController.");
}

internal static class TestDecisions
{
    public static Decision Low(double riskScore = 5) => new(
        DecisionId: Guid.NewGuid(),
        RequestId: Guid.NewGuid(),
        ReasoningTypeApplied: ReasoningType.DeterministicReasoning,
        SelectedHypothesis: "test hypothesis",
        RejectedHypotheses: [],
        EvidenceRefs: [],
        Confidence: 0.9,
        Explanation: new Explanation("test", [], [], [], "test", []),
        TradeOffs: "none",
        RiskScore: riskScore,
        Reproducible: true,
        OccurredAt: DateTimeOffset.UtcNow);
}

/// <summary>
/// Simulates a mid-iteration cancellation: cancels the shared <paramref name="cancellationTokenSource"/>
/// (the same source supplying <c>RunIterationAsync</c>'s own token) and then throws
/// <see cref="OperationCanceledException"/> against that now-cancelled token — proving the
/// compensating Failed-state write (which must use <see cref="CancellationToken.None"/>, not the
/// caller's token) still succeeds even though the original failure's own token is cancelled.
/// </summary>
internal sealed class CancellingReasoningEngineClient(CancellationTokenSource cancellationTokenSource) : IReasoningEngineClient
{
    public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default)
    {
        cancellationTokenSource.Cancel();
        throw new OperationCanceledException(cancellationTokenSource.Token);
    }

    public Task<ConfidenceGuardResult> CompareAsync(
        PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");
}

/// <summary>
/// WP-029: wraps a real <see cref="ILoopIterationStore"/>, recording every <c>state</c> value
/// passed to <see cref="UpdateStateAsync"/> — proves the Evaluating/Improving transitions (§19.1,
/// WP-028 Decision 4's reserved string values) are actually written, not just recorded as
/// <c>stepsTraversed</c> entries (final-adversarial-review finding).
/// </summary>
internal sealed class RecordingStateTransitionsLoopIterationStore(ILoopIterationStore inner) : ILoopIterationStore
{
    private readonly Lock _lock = new();

    public List<string> ObservedStates { get; } = [];

    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) =>
        inner.EnsureTableExistsAsync(cancellationToken);

    public Task InsertAsync(LoopIteration iteration, CancellationToken cancellationToken = default) =>
        inner.InsertAsync(iteration, cancellationToken);

    public Task UpdateStateAsync(Guid iterationId, string state, int[] stepsTraversed, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ObservedStates.Add(state);
        }

        return inner.UpdateStateAsync(iterationId, state, stepsTraversed, cancellationToken);
    }

    public Task CompleteAsync(
        Guid iterationId, string state, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        inner.CompleteAsync(iterationId, state, outcome, stepsTraversed, cancellationToken);

    public Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(iterationId, cancellationToken);

    public Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(cancellationToken);
}

/// <summary>
/// Wraps a real <see cref="ILoopIterationStore"/>, delegating everything except
/// <see cref="CompleteAsync"/>, which always throws — proves a persistence failure during the
/// catch block's compensating write preserves the original exception via AggregateException
/// rather than replacing it (CodeRabbit R1 finding #3).
/// </summary>
internal sealed class ThrowingOnCompleteLoopIterationStore(ILoopIterationStore inner) : ILoopIterationStore
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) =>
        inner.EnsureTableExistsAsync(cancellationToken);

    public Task InsertAsync(LoopIteration iteration, CancellationToken cancellationToken = default) =>
        inner.InsertAsync(iteration, cancellationToken);

    public Task UpdateStateAsync(Guid iterationId, string state, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        inner.UpdateStateAsync(iterationId, state, stepsTraversed, cancellationToken);

    public Task CompleteAsync(
        Guid iterationId, string state, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated terminal-persistence failure.");

    public Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(iterationId, cancellationToken);

    public Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(cancellationToken);
}

internal sealed class RecordingLoopIterationStartedEventPublisher : ILoopIterationStartedEventPublisher
{
    // CodeRabbit R2: RunIterationAsync_TwoConcurrentCallsProduceTwoIndependentIterationIds calls
    // PublishLoopIterationStarted from concurrent RunIterationAsync invocations — List<T>.Add is
    // not thread-safe, so a plain list could lose an entry or corrupt its internal state.
    private readonly Lock _lock = new();

    public List<(Guid IterationId, string TriggerSource, int EntryStep)> Published { get; } = [];

    public void PublishLoopIterationStarted(Guid iterationId, string triggerSource, int entryStep)
    {
        lock (_lock)
        {
            Published.Add((iterationId, triggerSource, entryStep));
        }
    }
}

/// <summary>
/// Mirrors <see cref="StateCapturingLoopIterationCompletedEventPublisher"/>'s exact precedent —
/// proves <c>LoopIterationStarted</c> is published strictly after the initial Triggered-state
/// insert, not before or concurrently with it.
/// </summary>
internal sealed class StateCapturingLoopIterationStartedEventPublisher(ILoopIterationStore store) : ILoopIterationStartedEventPublisher
{
    public List<bool> IterationExistedAtPublishTime { get; } = [];

    public void PublishLoopIterationStarted(Guid iterationId, string triggerSource, int entryStep) =>
        IterationExistedAtPublishTime.Add(store.GetByIdAsync(iterationId, CancellationToken.None).GetAwaiter().GetResult() is not null);
}

internal sealed class RecordingLoopIterationCompletedEventPublisher : ILoopIterationCompletedEventPublisher
{
    public List<(Guid IterationId, int[] StepsTraversed, string Outcome)> Published { get; } = [];

    public void PublishLoopIterationCompleted(Guid iterationId, int[] stepsTraversed, string outcome) =>
        Published.Add((iterationId, stepsTraversed, outcome));
}

/// <summary>
/// Mirrors <see cref="StateCapturingTaskStartedEventPublisher"/>'s exact precedent — proves
/// <c>LoopIterationCompleted</c> is published strictly after the Completed transition's
/// persistence write, not before or concurrently with it.
/// </summary>
internal sealed class StateCapturingLoopIterationCompletedEventPublisher(ILoopIterationStore store) : ILoopIterationCompletedEventPublisher
{
    public List<string?> ObservedStateAtPublishTime { get; } = [];

    public void PublishLoopIterationCompleted(Guid iterationId, int[] stepsTraversed, string outcome) =>
        ObservedStateAtPublishTime.Add(store.GetByIdAsync(iterationId, CancellationToken.None).GetAwaiter().GetResult()!.State);
}

/// <summary>Proves a LoopIterationStarted publication failure is compensated by a Failed terminal write, not left unpersisted.</summary>
internal sealed class ThrowingLoopIterationStartedEventPublisher : ILoopIterationStartedEventPublisher
{
    public Guid? LastIterationId { get; private set; }

    public void PublishLoopIterationStarted(Guid iterationId, string triggerSource, int entryStep)
    {
        LastIterationId = iterationId;
        throw new InvalidOperationException("Simulated LoopIterationStarted publication failure.");
    }
}

/// <summary>
/// Wraps a real <see cref="ILoopIterationStore"/>, delegating everything except
/// <see cref="CompleteAsync"/> when called with <c>state == "Completed"</c> (the successful
/// terminal write), which always throws — the compensating <c>state == "Failed"</c> write still
/// delegates to the real store and succeeds. Proves a successful-terminal-persistence failure is
/// compensated by a Failed write, not left stuck at an intermediate state.
/// </summary>
internal sealed class ThrowingOnSuccessfulCompleteLoopIterationStore(ILoopIterationStore inner) : ILoopIterationStore
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) =>
        inner.EnsureTableExistsAsync(cancellationToken);

    public Task InsertAsync(LoopIteration iteration, CancellationToken cancellationToken = default) =>
        inner.InsertAsync(iteration, cancellationToken);

    public Task UpdateStateAsync(Guid iterationId, string state, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        inner.UpdateStateAsync(iterationId, state, stepsTraversed, cancellationToken);

    public Task CompleteAsync(
        Guid iterationId, string state, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        state == "Completed"
            ? throw new InvalidOperationException("Simulated successful-terminal-persistence failure.")
            : inner.CompleteAsync(iterationId, state, outcome, stepsTraversed, cancellationToken);

    public Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(iterationId, cancellationToken);

    public Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(cancellationToken);
}

/// <summary>Proves LoopController persists Failed state and never publishes LoopIterationCompleted when a step throws.</summary>
internal sealed class ThrowingReasoningEngineClient : IReasoningEngineClient
{
    public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated Reasoning Engine failure.");

    public Task<ConfidenceGuardResult> CompareAsync(
        PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");

    public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not used by WP-028's LoopController.");
}

/// <summary>
/// Mirrors <see cref="StateCapturingTaskStartedEventPublisher"/>'s exact precedent — proves
/// <c>RollbackExecuted</c> is published strictly after the rollback transition's persistence
/// write, not before or concurrently with it.
/// </summary>
internal sealed class StateCapturingRollbackExecutedEventPublisher(DispatchedTaskStore store) : IRollbackExecutedEventPublisher
{
    public List<TaskLifecycleState> ObservedStatesAtPublishTime { get; } = [];

    public void PublishRollbackExecuted(Guid taskId, string rollbackPathUsed) =>
        ObservedStatesAtPublishTime.Add(store.GetByIdAsync(taskId, CancellationToken.None).GetAwaiter().GetResult()!.State);
}

/// <summary>WP-029: thread-safe recorder for <c>OperationalModeChanged</c>, mirroring <see cref="RecordingLoopIterationStartedEventPublisher"/>'s lock-guarded precedent (CodeRabbit R2).</summary>
internal sealed class RecordingOperationalModeChangedEventPublisher : IOperationalModeChangedEventPublisher
{
    private readonly Lock _lock = new();

    public List<(OperationalMode FromMode, OperationalMode ToMode, string ChangedBy)> Published { get; } = [];

    public void PublishOperationalModeChanged(OperationalMode fromMode, OperationalMode toMode, string changedBy)
    {
        lock (_lock)
        {
            Published.Add((fromMode, toMode, changedBy));
        }
    }
}

/// <summary>WP-029: thread-safe recorder for <c>LoopIterationEvaluated</c>, mirroring <see cref="RecordingLoopIterationStartedEventPublisher"/>'s lock-guarded precedent.</summary>
internal sealed class RecordingLoopIterationEvaluatedEventPublisher : ILoopIterationEvaluatedEventPublisher
{
    private readonly Lock _lock = new();

    public List<(Guid IterationId, double? LoopHealthScore)> Published { get; } = [];

    public void PublishLoopIterationEvaluated(Guid iterationId, double? loopHealthScore)
    {
        lock (_lock)
        {
            Published.Add((iterationId, loopHealthScore));
        }
    }
}

/// <summary>
/// Claude Code Review finding fix: wraps a real <see cref="ILoopIterationStore"/>, delegating
/// everything except <see cref="UpdateStateAsync"/> when called with <paramref name="throwingState"/>,
/// which always throws — proves a failure inside <c>RunSelfEvaluateAndImproveAsync</c>'s own
/// persistence writes (e.g. the "Improving" write) is compensated by the existing Failed-terminal-
/// write pattern, and specifically that <c>LoopIterationEvaluated</c> is never published for an
/// iteration that ultimately fails.
/// </summary>
internal sealed class ThrowingOnUpdateStateLoopIterationStore(ILoopIterationStore inner, string throwingState) : ILoopIterationStore
{
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) =>
        inner.EnsureTableExistsAsync(cancellationToken);

    public Task InsertAsync(LoopIteration iteration, CancellationToken cancellationToken = default) =>
        inner.InsertAsync(iteration, cancellationToken);

    public Task UpdateStateAsync(Guid iterationId, string state, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        state == throwingState
            ? throw new InvalidOperationException($"Simulated {throwingState} state-write failure.")
            : inner.UpdateStateAsync(iterationId, state, stepsTraversed, cancellationToken);

    public Task CompleteAsync(
        Guid iterationId, string state, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        inner.CompleteAsync(iterationId, state, outcome, stepsTraversed, cancellationToken);

    public Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(iterationId, cancellationToken);

    public Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(cancellationToken);
}

/// <summary>
/// Claude Code Review finding fix: wraps a real <see cref="ILoopIterationStore"/> and doubles as
/// an <see cref="ILoopIterationEvaluatedEventPublisher"/>, recording both the state passed to every
/// <see cref="UpdateStateAsync"/> call and each <c>LoopIterationEvaluated</c> publication into one
/// shared, ordered list — proves the publish genuinely happens after the "Improving" write, not
/// merely that both occurred somewhere during the run.
/// </summary>
internal sealed class EvaluatedOrderingRecorder(ILoopIterationStore inner) : ILoopIterationStore, ILoopIterationEvaluatedEventPublisher
{
    public List<string> ObservedOrder { get; } = [];

    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) =>
        inner.EnsureTableExistsAsync(cancellationToken);

    public Task InsertAsync(LoopIteration iteration, CancellationToken cancellationToken = default) =>
        inner.InsertAsync(iteration, cancellationToken);

    public async Task UpdateStateAsync(Guid iterationId, string state, int[] stepsTraversed, CancellationToken cancellationToken = default)
    {
        // CodeRabbit finding: record only after the delegated write actually completes, not at
        // call-time — otherwise a future LoopController regression that fires this write without
        // awaiting it would still produce the "correct" recorded order, defeating the point of
        // this ordering proof.
        await inner.UpdateStateAsync(iterationId, state, stepsTraversed, cancellationToken);
        ObservedOrder.Add(state);
    }

    public Task CompleteAsync(
        Guid iterationId, string state, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default) =>
        inner.CompleteAsync(iterationId, state, outcome, stepsTraversed, cancellationToken);

    public Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(iterationId, cancellationToken);

    public Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(cancellationToken);

    public void PublishLoopIterationEvaluated(Guid iterationId, double? loopHealthScore) => ObservedOrder.Add("LoopIterationEvaluated");
}

/// <summary>
/// Claude Code Review finding fix: wraps a real <see cref="IOperationalModeStore"/>, counting
/// <see cref="SetCurrentModeAsync"/> calls — proves <c>SetOperationalModeAsync</c>'s idempotency
/// short-circuit genuinely skips the write for a no-op mode request, not merely the event.
/// </summary>
internal sealed class CallCountingOperationalModeStore(IOperationalModeStore inner) : IOperationalModeStore
{
    public int SetCurrentModeAsyncCallCount { get; private set; }

    public Task EnsureTableExistsAsync(CancellationToken cancellationToken = default) =>
        inner.EnsureTableExistsAsync(cancellationToken);

    public Task<OperationalMode> GetCurrentModeAsync(CancellationToken cancellationToken = default) =>
        inner.GetCurrentModeAsync(cancellationToken);

    public Task<OperationalMode> SetCurrentModeAsync(OperationalMode mode, CancellationToken cancellationToken = default)
    {
        SetCurrentModeAsyncCallCount++;
        return inner.SetCurrentModeAsync(mode, cancellationToken);
    }
}

// Post-Roadmap WP-A: ExecutionCoordinator's execution collaborators (ITaskExecutionClient from
// EOS.Contracts; ITaskCompletedEventPublisher / ITaskBlockedEventPublisher from EOS.Orchestrator).

/// <summary>Executor double for tests that exercise dispatch only — must never be invoked.</summary>
internal sealed class NeverCalledTaskExecutionClient : ITaskExecutionClient
{
    public Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("ExecuteAsync must not be called by this test.");
}

/// <summary>Executor double returning fixed evidence references, recording every invocation.</summary>
internal sealed class FixedEvidenceTaskExecutionClient(params string[] evidenceRefs) : ITaskExecutionClient
{
    public List<Guid> ExecutedTaskIds { get; } = [];

    public Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        ExecutedTaskIds.Add(task.TaskId);
        return Task.FromResult(new TaskExecutionResult(evidenceRefs));
    }
}

/// <summary>Executor double that fails like a role that could not produce valid evidence.</summary>
internal sealed class ThrowingTaskExecutionClient(string message = "The produced diff is not a valid, in-scope unified diff.") : ITaskExecutionClient
{
    public int CallCount { get; private set; }

    public Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        CallCount++;
        throw new InvalidOperationException(message);
    }
}

/// <summary>Executor double that cancels the supplied token mid-execution and then observes it.</summary>
internal sealed class CancellingTaskExecutionClient(CancellationTokenSource cancellationTokenSource) : ITaskExecutionClient
{
    public async Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        await cancellationTokenSource.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("unreachable");
    }
}

internal sealed class RecordingTaskCompletedEventPublisher : ITaskCompletedEventPublisher
{
    public List<(Guid TaskId, string[] EvidenceRefs)> Published { get; } = [];

    public void PublishTaskCompleted(Guid taskId, string[] evidenceRefs) => Published.Add((taskId, evidenceRefs));
}

/// <summary>Captures the persisted Task state at the moment TaskCompleted is published (persist-then-publish proof).</summary>
internal sealed class StateCapturingTaskCompletedEventPublisher(DispatchedTaskStore store) : ITaskCompletedEventPublisher
{
    public List<TaskLifecycleState> ObservedStatesAtPublishTime { get; } = [];

    public void PublishTaskCompleted(Guid taskId, string[] evidenceRefs) =>
        ObservedStatesAtPublishTime.Add(store.GetByIdAsync(taskId, CancellationToken.None).GetAwaiter().GetResult()!.State);
}

internal sealed class RecordingTaskBlockedEventPublisher : ITaskBlockedEventPublisher
{
    public List<(Guid TaskId, string Reason)> Published { get; } = [];

    public void PublishTaskBlocked(Guid taskId, string reason) => Published.Add((taskId, reason));
}

/// <summary>
/// Re-enters the LoopController with a "Failure" trigger on every TaskBlocked — the exact wiring
/// Program.cs already has for TaskBlockedPayload — and fails loudly if invoked more than once,
/// so any recursion of the Failure-triggered iteration fails the test instead of looping.
/// </summary>
internal sealed class FailureTriggeringTaskBlockedEventPublisher : ITaskBlockedEventPublisher
{
    public LoopController? Controller { get; set; }

    public int Invocations { get; private set; }

    public void PublishTaskBlocked(Guid taskId, string reason)
    {
        Invocations++;
        if (Invocations > 1)
        {
            throw new InvalidOperationException("TaskBlocked was published more than once — the Failure-triggered iteration recursed.");
        }

        Controller!.RunIterationAsync(new TriggerContext("Failure", taskId.ToString()), CancellationToken.None).GetAwaiter().GetResult();
    }
}

/// <summary>Allows every action except the named ActionType, which it denies — isolates one gate.</summary>
internal sealed class DenyActionTypeProtectionClient(string deniedActionType) : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) =>
        action.ActionType == deniedActionType
            ? new ValidationResult(ProtectionVerdict.Deny, RiskTier.High, $"{deniedActionType} denied by test.")
            : new ValidationResult(ProtectionVerdict.Allow, RiskTier.Low, null);
}

// ADR-009: ExecutionCoordinator's Universal Gate collaborator (IUniversalGateClient from EOS.Contracts).

/// <summary>Gate double that reports Gates 1–2 as passed and records the evidence it was asked to gate.</summary>
internal sealed class PassingUniversalGateClient : IUniversalGateClient
{
    public List<(Guid TaskId, string[] EvidenceRefs)> Evaluated { get; } = [];

    public Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default)
    {
        Evaluated.Add((task.TaskId, [.. evidenceRefs]));
        var result = new UniversalGateResult(
            new GateStepResult(GateStepStatus.Passed, "Built"),
            new GateStepResult(GateStepStatus.Passed, "Tested"));
        return Task.FromResult(new UniversalGateDecision(true, null, result));
    }
}

/// <summary>Gate double that reports a failed Universal Gate with the given Rule Engine reason.</summary>
internal sealed class FailingUniversalGateClient(string reason) : IUniversalGateClient
{
    public int CallCount { get; private set; }

    public Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default)
    {
        CallCount++;
        var result = new UniversalGateResult(
            new GateStepResult(GateStepStatus.Failed, reason),
            new GateStepResult(GateStepStatus.NotApplicable, "Not run — Gate 1 failed."));
        return Task.FromResult(new UniversalGateDecision(false, reason, result));
    }
}

/// <summary>Gate double that fails to run at all (e.g. the isolated copy could not be created).</summary>
internal sealed class ThrowingUniversalGateClient(string message) : IUniversalGateClient
{
    public Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default) =>
        throw new IOException(message);
}

/// <summary>Gate double that cancels the supplied token mid-evaluation and then observes it.</summary>
internal sealed class CancellingUniversalGateClient(CancellationTokenSource cancellationTokenSource) : IUniversalGateClient
{
    public async Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default)
    {
        await cancellationTokenSource.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("unreachable");
    }
}

/// <summary>Gate double for tests whose path must block before any gate runs — must never be invoked.</summary>
internal sealed class NeverCalledUniversalGateClient : IUniversalGateClient
{
    public Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("EvaluateAsync must not be called by this test.");
}

// Qodo #1 (PR #29): doubles that complete normally but cancel the caller's token before returning,
// so the coordinator reaches its terminal write with an already-cancelled token — deterministic,
// no timing involved.

/// <summary>Executor double returning the given evidence after cancelling the supplied token.</summary>
internal sealed class EvidenceThenCancelTaskExecutionClient(CancellationTokenSource cancellationTokenSource, params string[] evidenceRefs) : ITaskExecutionClient
{
    public async Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        await cancellationTokenSource.CancelAsync();
        return new TaskExecutionResult(evidenceRefs);
    }
}

/// <summary>Executor double that fails like a role after cancelling the supplied token.</summary>
internal sealed class ThrowThenCancelTaskExecutionClient(CancellationTokenSource cancellationTokenSource) : ITaskExecutionClient
{
    public async Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        await cancellationTokenSource.CancelAsync();
        throw new InvalidOperationException("role failed after cancellation was requested");
    }
}

/// <summary>Gate double returning the given decision after cancelling the supplied token.</summary>
internal sealed class DecideThenCancelUniversalGateClient(CancellationTokenSource cancellationTokenSource, bool passed) : IUniversalGateClient
{
    public async Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default)
    {
        await cancellationTokenSource.CancelAsync();
        var status = passed ? GateStepStatus.Passed : GateStepStatus.Failed;
        var result = new UniversalGateResult(new GateStepResult(status, "decided"), new GateStepResult(GateStepStatus.NotApplicable, "n/a"));
        return new UniversalGateDecision(passed, passed ? null : "Universal Gate 1 (build/static analysis) failed: decided", result);
    }
}
