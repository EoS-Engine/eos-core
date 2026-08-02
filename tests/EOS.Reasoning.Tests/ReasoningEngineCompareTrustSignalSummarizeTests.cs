using EOS.Contracts;
using EOS.Reasoning;
using EOS.SDK;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Reasoning.Tests;

public class ReasoningEngineCompareTrustSignalSummarizeTests
{
    private static ReasoningEngine CreateEngine(IAIProviderClient aiProviderClient)
    {
        return new ReasoningEngine(
            aiProviderClient,
            NeverCalledContextAcquisitionProvider.Instance,
            new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: 0.3),
            NoOpDecisionMadeEventPublisher.Instance,
            NoOpLowConfidenceDecisionFlaggedEventPublisher.Instance,
            NoOpContextExpansionRequestedEventPublisher.Instance,
            NullLogger<ReasoningEngine>.Instance);
    }

    private static PipelineRecord CreateRecord(
        Guid? knowledgeGraphRef = null, string[]? domainTags = null, PipelineRecordStatus status = PipelineRecordStatus.Active)
    {
        return new PipelineRecord(
            RecordId: Guid.NewGuid(),
            Stage: PipelineStage.Lesson,
            KnowledgeGraphRef: knowledgeGraphRef ?? Guid.NewGuid(),
            SourceLessonIds: [],
            DomainTags: domainTags ?? [],
            CreatedAt: DateTimeOffset.UtcNow,
            LastAdvancedAt: DateTimeOffset.UtcNow,
            ApprovalRefs: [],
            RoiEvaluationRef: null,
            TrustScore: 0.5,
            ConfidenceScore: 0.5,
            Status: status);
    }

    // --- compare() ---

    [Fact]
    public async Task CompareAsync_Throws_WhenSubjectIsQuarantined()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));
        var subject = CreateRecord(status: PipelineRecordStatus.Quarantined);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.CompareAsync(subject, []));
    }

    [Theory]
    [InlineData(PipelineRecordStatus.Quarantined)]
    [InlineData(PipelineRecordStatus.Archived)]
    public async Task CompareAsync_Throws_WhenAnyCandidateIsQuarantinedOrArchived(PipelineRecordStatus status)
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));
        var subject = CreateRecord();
        var candidate = CreateRecord(status: status);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.CompareAsync(subject, [candidate]));
    }

    [Fact]
    public async Task CompareAsync_AcceptsCandidate_WhenKnowledgeGraphRefMatches()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));
        var sharedRef = Guid.NewGuid();
        var subject = CreateRecord(knowledgeGraphRef: sharedRef);
        var candidate = CreateRecord(knowledgeGraphRef: sharedRef);

        var result = await engine.CompareAsync(subject, [candidate]);

        Assert.Equal(1.0, result.Confidence);
        Assert.Single(result.AcceptedMatches);
        Assert.Empty(result.RejectedMatches);
    }

    [Fact]
    public async Task CompareAsync_AcceptsCandidate_WhenDomainTagsOverlap()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));
        var subject = CreateRecord(domainTags: ["backend", "api"]);
        var candidate = CreateRecord(domainTags: ["api", "security"]);

        var result = await engine.CompareAsync(subject, [candidate]);

        Assert.Single(result.AcceptedMatches);
    }

    [Fact]
    public async Task CompareAsync_RejectsCandidate_WhenNoStructuralSignalIsShared()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));
        var subject = CreateRecord(domainTags: ["backend"]);
        var candidate = CreateRecord(domainTags: ["frontend"]);

        var result = await engine.CompareAsync(subject, [candidate]);

        Assert.Empty(result.AcceptedMatches);
        var rejected = Assert.Single(result.RejectedMatches);
        Assert.Equal(candidate, rejected.Record);
        Assert.False(string.IsNullOrWhiteSpace(rejected.RejectionReason));
    }

    [Fact]
    public async Task CompareAsync_NeverDropsACandidate_AcceptedUnionRejectedEqualsAllInputs()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));
        var subject = CreateRecord(domainTags: ["backend"]);
        var related = CreateRecord(domainTags: ["backend"]);
        var unrelated = CreateRecord(domainTags: ["frontend"]);

        var result = await engine.CompareAsync(subject, [related, unrelated]);

        var allReturned = result.AcceptedMatches.Concat(result.RejectedMatches.Select(r => r.Record)).ToArray();
        Assert.Equal(2, allReturned.Length);
        Assert.Contains(related, allReturned);
        Assert.Contains(unrelated, allReturned);
        Assert.InRange(result.Confidence, 0.0, 1.0);
    }

    // --- get_trust_signal() ---

    [Fact]
    public async Task GetTrustSignalAsync_Throws_WhenSourceRoleIsEmpty()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.GetTrustSignalAsync("   "));
    }

    [Fact]
    public async Task GetTrustSignalAsync_ReturnsNeutralDefault_WhenNoHistoryExists()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));

        var signal = await engine.GetTrustSignalAsync("PrincipalEngineer");

        Assert.Equal("PrincipalEngineer", signal.SourceRole);
        Assert.Equal(0.5, signal.Score);
        Assert.False(string.IsNullOrWhiteSpace(signal.EvidenceRef));
    }

    // --- summarize() ---

    [Fact]
    public async Task SummarizeAsync_Throws_WhenContentIsEmpty()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "unused"));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.SummarizeAsync("   "));
    }

    [Fact]
    public async Task SummarizeAsync_ReturnsInferenceOutput_WhenSuccessful()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: true, output: "a condensed summary"));

        var summary = await engine.SummarizeAsync("some long content to condense");

        Assert.Equal("a condensed summary", summary.Content);
    }

    [Fact]
    public async Task SummarizeAsync_IncludesSizeBudgetInPrompt_WhenSupplied()
    {
        var stubAIProviderClient = new CapturingAIProviderClient(succeed: true, output: "summary");
        var engine = CreateEngine(stubAIProviderClient);

        await engine.SummarizeAsync("content", sizeBudget: 200);

        Assert.Contains("200", stubAIProviderClient.LastRequest!.Payload);
    }

    [Fact]
    public async Task SummarizeAsync_ThrowsInternalError_WhenInferenceFails()
    {
        var engine = CreateEngine(new StubAIProviderClient(succeed: false, output: null));

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(() => engine.SummarizeAsync("content"));

        Assert.Equal(ReasoningFailureMode.InternalError, exception.FailureMode);
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

    private sealed class NoOpDecisionMadeEventPublisher : IDecisionMadeEventPublisher
    {
        public static readonly NoOpDecisionMadeEventPublisher Instance = new();

        public void PublishDecisionMade(Guid decisionId, Guid requestId, double confidence, double riskScore, ReasoningType reasoningTypeApplied)
        {
        }
    }

    private sealed class NoOpLowConfidenceDecisionFlaggedEventPublisher : ILowConfidenceDecisionFlaggedEventPublisher
    {
        public static readonly NoOpLowConfidenceDecisionFlaggedEventPublisher Instance = new();

        public void PublishLowConfidenceDecisionFlagged(Guid decisionId, Guid correlationId, double confidence, double threshold)
        {
        }
    }

    private sealed class NoOpContextExpansionRequestedEventPublisher : IContextExpansionRequestedEventPublisher
    {
        public static readonly NoOpContextExpansionRequestedEventPublisher Instance = new();

        public void PublishContextExpansionRequested(Guid requestId, ReasoningContextScope originalScope, ReasoningContextScope expandedScope)
        {
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
}
