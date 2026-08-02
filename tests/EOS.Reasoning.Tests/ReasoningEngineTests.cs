using EOS.Contracts;
using EOS.Reasoning;
using EOS.SDK;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Reasoning.Tests;

public class ReasoningEngineTests
{
    private static ReasoningRequest CreateRequest(string goal)
    {
        return new ReasoningRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            Goal: goal,
            RequestingRole: "PrincipalEngineer");
    }

    private static ReasoningEngine CreateEngine(
        IAIProviderClient aiProviderClient,
        IContextAcquisitionProvider contextAcquisitionProvider,
        ReasoningEngineOptions? options = null,
        IDecisionMadeEventPublisher? decisionMadeEventPublisher = null,
        ILowConfidenceDecisionFlaggedEventPublisher? lowConfidenceDecisionFlaggedEventPublisher = null,
        IContextExpansionRequestedEventPublisher? contextExpansionRequestedEventPublisher = null)
    {
        return new ReasoningEngine(
            aiProviderClient,
            contextAcquisitionProvider,
            options ?? new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: 0.3),
            decisionMadeEventPublisher ?? new CapturingDecisionMadeEventPublisher(),
            lowConfidenceDecisionFlaggedEventPublisher ?? new CapturingLowConfidenceDecisionFlaggedEventPublisher(),
            contextExpansionRequestedEventPublisher ?? new CapturingContextExpansionRequestedEventPublisher(),
            NullLogger<ReasoningEngine>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReasonAsync_ThrowsInvalidGoal_WhenGoalIsEmptyOrWhitespace(string goal)
    {
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "unused"), NeverCalledContextAcquisitionProvider.Instance);

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(
            () => engine.ReasonAsync(CreateRequest(goal)));

        Assert.Equal(ReasoningFailureMode.InvalidGoal, exception.FailureMode);
    }

    [Fact]
    public async Task ReasonAsync_ThrowsInternalError_WhenInferenceFails()
    {
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: false, output: null), NeverCalledContextAcquisitionProvider.Instance);

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(
            () => engine.ReasonAsync(CreateRequest("explain the SOLID principles")));

        Assert.Equal(ReasoningFailureMode.InternalError, exception.FailureMode);
    }

    [Fact]
    public async Task ReasonAsync_ReturnsASingleDecision_WithNonEmptyEvidenceConfidenceAndExplanation()
    {
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "SOLID stands for..."), NeverCalledContextAcquisitionProvider.Instance);
        var request = CreateRequest("explain the SOLID principles");

        var decisions = await engine.ReasonAsync(request);

        var decision = Assert.Single(decisions);
        Assert.Equal(request.RequestId, decision.RequestId);
        Assert.Equal(ReasoningType.EngineeringReasoning, decision.ReasoningTypeApplied);
        Assert.Equal("SOLID stands for...", decision.SelectedHypothesis);
        Assert.Empty(decision.RejectedHypotheses);
        Assert.NotEmpty(decision.EvidenceRefs);
        Assert.True(decision.Confidence > 0);
        Assert.NotNull(decision.Explanation);
        Assert.False(string.IsNullOrWhiteSpace(decision.Explanation.Why));
        Assert.NotEmpty(decision.Explanation.EvidenceUsed);
        Assert.NotEmpty(decision.Explanation.Risks);
    }

    [Theory]
    [InlineData(ReasoningType.DeterministicReasoning)]
    [InlineData(ReasoningType.AnalyticalReasoning)]
    [InlineData(ReasoningType.RuleBasedReasoning)]
    [InlineData(ReasoningType.GoalOrientedReasoning)]
    [InlineData(ReasoningType.ContextualReasoning)]
    [InlineData(ReasoningType.ArchitecturalReasoning)]
    [InlineData(ReasoningType.EngineeringReasoning)]
    [InlineData(ReasoningType.DiagnosticReasoning)]
    [InlineData(ReasoningType.RootCauseAnalysis)]
    [InlineData(ReasoningType.ComparativeReasoning)]
    [InlineData(ReasoningType.RiskReasoning)]
    [InlineData(ReasoningType.OptimizationReasoning)]
    [InlineData(ReasoningType.StrategicReasoning)]
    public async Task ReasonAsync_AppliesTheExplicitlyRequestedReasoningType(ReasoningType requestedType)
    {
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), NeverCalledContextAcquisitionProvider.Instance);
        var request = CreateRequest("a goal") with { ReasoningType = requestedType };

        var decisions = await engine.ReasonAsync(request);

        var decision = Assert.Single(decisions);
        Assert.Equal(requestedType, decision.ReasoningTypeApplied);
    }

    [Fact]
    public async Task ReasonAsync_DefaultsToEngineeringReasoning_WhenReasoningTypeIsNotSpecified()
    {
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), NeverCalledContextAcquisitionProvider.Instance);
        var request = CreateRequest("a goal");

        var decisions = await engine.ReasonAsync(request);

        var decision = Assert.Single(decisions);
        Assert.Equal(ReasoningType.EngineeringReasoning, decision.ReasoningTypeApplied);
    }

    [Fact]
    public async Task ReasonAsync_AcquiresContext_WhenContextScopeIsSupplied()
    {
        var contextAcquisitionProvider = new CapturingContextAcquisitionProvider();
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), contextAcquisitionProvider);
        var scope = new ReasoningContextScope(DomainTags: ["backend"], ProjectScope: null, Budget: 4096);
        var request = CreateRequest("a goal") with { ContextScope = scope };

        await engine.ReasonAsync(request);

        Assert.Equal(scope, contextAcquisitionProvider.LastScope);
    }

    [Fact]
    public async Task ReasonAsync_DoesNotAcquireContext_WhenContextScopeIsNotSupplied()
    {
        // NeverCalledContextAcquisitionProvider throws if invoked — every other test in this
        // file already proves this path, but this test names the guarantee explicitly.
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), NeverCalledContextAcquisitionProvider.Instance);
        var request = CreateRequest("a goal");

        var decisions = await engine.ReasonAsync(request);

        Assert.Single(decisions);
    }

    [Fact]
    public async Task ReasonAsync_IncludesConstraintsInInferencePayload_WhenConstraintsAreSupplied()
    {
        var stubAIProviderClient = new CapturingAIProviderClient(succeed: true, output: "an answer");
        var engine = CreateEngine(stubAIProviderClient, NeverCalledContextAcquisitionProvider.Instance);
        var request = CreateRequest("a goal") with { Constraints = ["must be secure", "must be cheap"] };

        await engine.ReasonAsync(request);

        Assert.Contains("must be secure", stubAIProviderClient.LastRequest!.Payload);
        Assert.Contains("must be cheap", stubAIProviderClient.LastRequest!.Payload);
    }

    [Fact]
    public async Task ReasonAsync_DoesNotIncludeConstraintsInInferencePayload_WhenConstraintsAreNotSupplied()
    {
        var stubAIProviderClient = new CapturingAIProviderClient(succeed: true, output: "an answer");
        var engine = CreateEngine(stubAIProviderClient, NeverCalledContextAcquisitionProvider.Instance);
        var request = CreateRequest("a goal");

        await engine.ReasonAsync(request);

        Assert.DoesNotContain("Constraints to respect", stubAIProviderClient.LastRequest!.Payload);
    }

    [Fact]
    public async Task ReasonAsync_ReturnsMultipleRankedDecisions_WhenInferenceOutputContainsMultipleCandidates()
    {
        var stubAIProviderClient = new CapturingAIProviderClient(
            succeed: true, output: "candidate one\n===CANDIDATE===\ncandidate two");
        var engine = CreateEngine(stubAIProviderClient, NeverCalledContextAcquisitionProvider.Instance);
        var request = CreateRequest("a goal");

        var decisions = await engine.ReasonAsync(request);

        Assert.Equal(2, decisions.Length);
        Assert.Equal("candidate one", decisions[0].SelectedHypothesis);
        Assert.Equal(["candidate two"], decisions[0].RejectedHypotheses);
        Assert.Equal("candidate two", decisions[1].SelectedHypothesis);
        Assert.Equal(["candidate one"], decisions[1].RejectedHypotheses);
    }

    [Fact]
    public async Task ReasonAsync_ThrowsMissingContext_WhenAcquiredContextIsEmpty()
    {
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), new EmptyContextAcquisitionProvider());
        var scope = new ReasoningContextScope(DomainTags: ["backend"], ProjectScope: null, Budget: 4096);
        var request = CreateRequest("a goal") with { ContextScope = scope };

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(() => engine.ReasonAsync(request));

        Assert.Equal(ReasoningFailureMode.MissingContext, exception.FailureMode);
    }

    [Fact]
    public async Task ReasonAsync_ThrowsMissingContext_WhenStillTruncatedAfterContextExpansion()
    {
        var expansionPublisher = new CapturingContextExpansionRequestedEventPublisher();
        var contextProvider = new AlwaysTruncatedContextAcquisitionProvider();
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), contextProvider,
            contextExpansionRequestedEventPublisher: expansionPublisher);
        var scope = new ReasoningContextScope(DomainTags: ["backend"], ProjectScope: null, Budget: 4096);
        var request = CreateRequest("a goal") with { ContextScope = scope };

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(() => engine.ReasonAsync(request));

        Assert.Equal(ReasoningFailureMode.MissingContext, exception.FailureMode);
        Assert.Equal(2, contextProvider.CallCount);
        Assert.Equal(1, expansionPublisher.CallCount);
    }

    [Fact]
    public async Task ReasonAsync_Succeeds_WhenContextExpansionResolvesTruncation()
    {
        var contextProvider = new ResolvesAfterOneExpansionContextAcquisitionProvider();
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), contextProvider);
        var scope = new ReasoningContextScope(DomainTags: ["backend"], ProjectScope: null, Budget: 4096);
        var request = CreateRequest("a goal") with { ContextScope = scope };

        var decisions = await engine.ReasonAsync(request);

        Assert.Single(decisions);
        Assert.Equal(2, contextProvider.CallCount);
    }

    [Fact]
    public async Task ReasonAsync_PublishesDecisionMade_ForEveryDecision()
    {
        var decisionMadePublisher = new CapturingDecisionMadeEventPublisher();
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), NeverCalledContextAcquisitionProvider.Instance,
            decisionMadeEventPublisher: decisionMadePublisher);

        await engine.ReasonAsync(CreateRequest("a goal"));

        Assert.Equal(1, decisionMadePublisher.CallCount);
    }

    [Fact]
    public async Task ReasonAsync_FlagsLowConfidence_WhenConfidenceBelowConfiguredFloor()
    {
        var lowConfidencePublisher = new CapturingLowConfidenceDecisionFlaggedEventPublisher();
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), NeverCalledContextAcquisitionProvider.Instance,
            options: new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: 0.9),
            lowConfidenceDecisionFlaggedEventPublisher: lowConfidencePublisher);

        var request = CreateRequest("a goal");
        await engine.ReasonAsync(request);

        Assert.Equal(1, lowConfidencePublisher.CallCount);
        Assert.Equal(0.5, lowConfidencePublisher.LastConfidence);
        Assert.Equal(request.CorrelationId, lowConfidencePublisher.LastCorrelationId);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.5)]
    public async Task ReasonAsync_DoesNotFlagLowConfidence_WhenConfidenceAtOrAboveConfiguredFloor(double floor)
    {
        var lowConfidencePublisher = new CapturingLowConfidenceDecisionFlaggedEventPublisher();
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "an answer"), NeverCalledContextAcquisitionProvider.Instance,
            options: new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: floor),
            lowConfidenceDecisionFlaggedEventPublisher: lowConfidencePublisher);

        await engine.ReasonAsync(CreateRequest("a goal"));

        Assert.Equal(0, lowConfidencePublisher.CallCount);
    }

    [Fact]
    public async Task ReasonAsync_AcquiresContext_BeforeThrowingInvalidGoal_WhenGoalIsEmptyAndContextScopeIsSupplied()
    {
        // §10: "Every request... passes through all applicable stages in order" — Stage 1
        // (Context Processing) precedes Stage 2 (Goal Understanding), so an empty goal combined
        // with a ContextScope still acquires context before InvalidGoal is raised. This test
        // pins that specification-ordered behavior rather than treating it as a defect.
        var contextProvider = new CapturingContextAcquisitionProvider();
        var engine = CreateEngine(
            new StubAIProviderClient(succeed: true, output: "unused"), contextProvider);
        var scope = new ReasoningContextScope(DomainTags: ["backend"], ProjectScope: null, Budget: 4096);
        var request = CreateRequest("   ") with { ContextScope = scope };

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(() => engine.ReasonAsync(request));

        Assert.Equal(ReasoningFailureMode.InvalidGoal, exception.FailureMode);
        Assert.Equal(scope, contextProvider.LastScope);
    }

    [Fact]
    public async Task ReasonAsync_ReducesContext_ForDeterministicReasoning()
    {
        var stubAIProviderClient = new CapturingAIProviderClient(succeed: true, output: "an answer");
        var contextProvider = new StaticContextAcquisitionProvider(["about apples", "about oranges", "about pears"]);
        var engine = CreateEngine(stubAIProviderClient, contextProvider);
        var scope = new ReasoningContextScope(DomainTags: null, ProjectScope: null, Budget: 4096);
        var request = CreateRequest("tell me about fruit") with
        {
            ReasoningType = ReasoningType.DeterministicReasoning,
            ContextScope = scope,
        };

        await engine.ReasonAsync(request);

        var payload = stubAIProviderClient.LastRequest!.Payload;
        var mentionedCount = new[] { "about apples", "about oranges", "about pears" }.Count(payload.Contains);
        Assert.Equal(1, mentionedCount);
    }

    private sealed class StaticContextAcquisitionProvider(string[] items) : IContextAcquisitionProvider
    {
        public Task<AcquiredContext> AcquireContextAsync(
            ReasoningContextScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AcquiredContext(items, Truncated: false));
    }

    private sealed class CapturingAIProviderClient(bool succeed, string? output) : IAIProviderClient
    {
        public InferenceRequest? LastRequest { get; private set; }

        public Task<InferenceResult> InferAsync(InferenceRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var result = succeed
                ? new InferenceResult(true, output, "stub-model", 10, 10, TimeSpan.FromMilliseconds(1), null, null)
                : new InferenceResult(false, null, null, null, null, null, InferenceErrorType.ProviderUnavailable, "stub failure");

            return Task.FromResult(result);
        }
    }

    private sealed class CapturingContextAcquisitionProvider : IContextAcquisitionProvider
    {
        public ReasoningContextScope? LastScope { get; private set; }

        public Task<AcquiredContext> AcquireContextAsync(
            ReasoningContextScope scope, CancellationToken cancellationToken = default)
        {
            LastScope = scope;
            return Task.FromResult(new AcquiredContext(["some relevant context item"], Truncated: false));
        }
    }

    private sealed class EmptyContextAcquisitionProvider : IContextAcquisitionProvider
    {
        public Task<AcquiredContext> AcquireContextAsync(
            ReasoningContextScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AcquiredContext([], Truncated: false));
    }

    private sealed class AlwaysTruncatedContextAcquisitionProvider : IContextAcquisitionProvider
    {
        public int CallCount { get; private set; }

        public Task<AcquiredContext> AcquireContextAsync(
            ReasoningContextScope scope, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AcquiredContext(["partial item"], Truncated: true));
        }
    }

    private sealed class ResolvesAfterOneExpansionContextAcquisitionProvider : IContextAcquisitionProvider
    {
        public int CallCount { get; private set; }

        public Task<AcquiredContext> AcquireContextAsync(
            ReasoningContextScope scope, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AcquiredContext(["item"], Truncated: CallCount == 1));
        }
    }

    private sealed class CapturingDecisionMadeEventPublisher : IDecisionMadeEventPublisher
    {
        public int CallCount { get; private set; }

        public void PublishDecisionMade(Guid decisionId, Guid requestId, double confidence, double riskScore, ReasoningType reasoningTypeApplied) =>
            CallCount++;
    }

    private sealed class CapturingLowConfidenceDecisionFlaggedEventPublisher : ILowConfidenceDecisionFlaggedEventPublisher
    {
        public int CallCount { get; private set; }
        public double? LastConfidence { get; private set; }
        public Guid? LastCorrelationId { get; private set; }

        public void PublishLowConfidenceDecisionFlagged(Guid decisionId, Guid correlationId, double confidence, double threshold)
        {
            CallCount++;
            LastCorrelationId = correlationId;
            LastConfidence = confidence;
        }
    }

    private sealed class CapturingContextExpansionRequestedEventPublisher : IContextExpansionRequestedEventPublisher
    {
        public int CallCount { get; private set; }

        public void PublishContextExpansionRequested(Guid requestId, ReasoningContextScope originalScope, ReasoningContextScope expandedScope) =>
            CallCount++;
    }

    private sealed class NeverCalledContextAcquisitionProvider : IContextAcquisitionProvider
    {
        public static readonly NeverCalledContextAcquisitionProvider Instance = new();

        public Task<AcquiredContext> AcquireContextAsync(
            ReasoningContextScope scope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called when ReasoningRequest.ContextScope is null.");
        }
    }

    private sealed class StubAIProviderClient(bool succeed, string? output) : IAIProviderClient
    {
        public Task<InferenceResult> InferAsync(InferenceRequest request, CancellationToken cancellationToken = default)
        {
            var result = succeed
                ? new InferenceResult(true, output, "stub-model", 10, 10, TimeSpan.FromMilliseconds(1), null, null)
                : new InferenceResult(false, null, null, null, null, null, InferenceErrorType.ProviderUnavailable, "stub failure");

            return Task.FromResult(result);
        }
    }
}
