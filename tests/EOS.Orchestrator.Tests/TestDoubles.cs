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
