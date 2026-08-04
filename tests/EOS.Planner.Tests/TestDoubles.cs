using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;

namespace EOS.Planner.Tests;

internal static class TestConnectionString
{
    public static string SqlServer =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");
}

internal sealed class AlwaysAllowProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Allow, RiskTier.Low, null);
}

internal sealed class AlwaysDenyProtectionClient : IProtectionClient
{
    public ValidationResult Validate(ActionRequest action) => new(ProtectionVerdict.Deny, RiskTier.Low, "Denied by test.");
}

internal sealed class NoOpGoalCreatedEventPublisher : IGoalCreatedEventPublisher
{
    public void PublishGoalCreated(Guid goalId, Guid? parentGoalId, string statement)
    {
    }
}

internal sealed class NoOpGoalCancelledEventPublisher : IGoalCancelledEventPublisher
{
    public void PublishGoalCancelled(Guid goalId, string reason)
    {
    }
}

internal sealed class NoOpGoalValidatedEventPublisher : IGoalValidatedEventPublisher
{
    public void PublishGoalValidated(Guid goalId, bool feasibilityResult)
    {
    }
}

internal sealed class NoOpTaskCreatedEventPublisher : ITaskCreatedEventPublisher
{
    public void PublishTaskCreated(Guid taskId, string[] competenciesRequired, int priority)
    {
    }
}

internal sealed class NoOpPlannerGeneratedEventPublisher : IPlannerGeneratedEventPublisher
{
    public void PublishPlannerGenerated(Guid planId, Guid taskGraphRef)
    {
    }
}

/// <summary>
/// Hand-rolled <see cref="IKnowledgeClient"/> stub (this repository uses no mocking framework)
/// returning a fixed set of <see cref="KnowledgeNode"/> instances from
/// <see cref="QueryAsync"/> regardless of the requested <see cref="MemoryType"/>/domain tags —
/// sufficient for <c>TaskGraphBuilder</c>'s own filtering logic, which is what these tests
/// exercise.
/// </summary>
internal sealed class FixedKnowledgeClient(IReadOnlyList<KnowledgeNode> nodes) : IKnowledgeClient
{
    public Task UpdateAsync(
        Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
        KnowledgeMetadata? metadata = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by TaskGraphBuilder.");

    public Task<IEnumerable<KnowledgeNode>> QueryAsync(
        MemoryType? type, string[]? domainTags, DateRange? range, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<KnowledgeNode>>(nodes);

    public Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by TaskGraphBuilder.");

    public Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by TaskGraphBuilder.");

    public Task<Guid> ConsolidateAsync(
        MemoryRef source, string reason, string[] evidenceRefs, bool suppressLessonLearned = false,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by TaskGraphBuilder.");
}

/// <summary>
/// Hand-rolled <see cref="IKnowledgeClient"/> stub whose <see cref="QueryAsync"/> always throws —
/// used to exercise <c>PlanningEngine.SubmitGoalAsync</c>'s decomposition-failure compensation
/// path (a genuine, if rare, failure mode: Memory/Knowledge infrastructure unavailable).
/// </summary>
internal sealed class ThrowingKnowledgeClient : IKnowledgeClient
{
    public Task UpdateAsync(
        Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
        KnowledgeMetadata? metadata = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by this test.");

    public Task<IEnumerable<KnowledgeNode>> QueryAsync(
        MemoryType? type, string[]? domainTags, DateRange? range, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated Knowledge infrastructure failure.");

    public Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by this test.");

    public Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by this test.");

    public Task<Guid> ConsolidateAsync(
        MemoryRef source, string reason, string[] evidenceRefs, bool suppressLessonLearned = false,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by this test.");
}

/// <summary>
/// Hand-rolled <see cref="IReasoningEngineClient"/> stub. <see cref="CallCount"/> lets the
/// ADR-PE003 enforcement test assert the bounded-delegation call count directly.
/// </summary>
internal sealed class CountingReasoningEngineClient(string selectedHypothesis) : IReasoningEngineClient
{
    public int CallCount { get; private set; }

    public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new[]
        {
            new Decision(
                DecisionId: Guid.NewGuid(),
                RequestId: request.RequestId,
                ReasoningTypeApplied: request.ReasoningType ?? ReasoningType.GoalOrientedReasoning,
                SelectedHypothesis: selectedHypothesis,
                RejectedHypotheses: [],
                EvidenceRefs: [],
                Confidence: 0.9,
                Explanation: new Explanation("test", [], [], [], "test", []),
                TradeOffs: "none",
                RiskScore: 0.0,
                Reproducible: true,
                OccurredAt: DateTimeOffset.UtcNow),
        });
    }

    public Task<ConfidenceGuardResult> CompareAsync(
        PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by TaskGraphBuilder.");

    public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by TaskGraphBuilder.");

    public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by TaskGraphBuilder.");
}
