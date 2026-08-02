using EOS.AIProvider;
using EOS.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Reasoning.Tests;

public class ReasoningEngineIntegrationTests
{
    [Fact]
    public async Task ReasonAsync_ExplainSOLIDPrinciples_ReturnsADecisionWithNonEmptyEvidenceConfidenceAndExplanation()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
        var aiProviderClient = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 4096, temperature: 0.2);
        var engine = new ReasoningEngine(
            aiProviderClient,
            NeverCalledContextAcquisitionProvider.Instance,
            new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: 0.3),
            new NoOpDecisionMadeEventPublisher(),
            new NoOpLowConfidenceDecisionFlaggedEventPublisher(),
            new NoOpContextExpansionRequestedEventPublisher(),
            NullLogger<ReasoningEngine>.Instance);
        var request = new ReasoningRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            Goal: "explain the SOLID principles",
            RequestingRole: "PrincipalEngineer");

        var decisions = await engine.ReasonAsync(request);

        var decision = Assert.Single(decisions);
        Assert.False(string.IsNullOrWhiteSpace(decision.SelectedHypothesis));
        Assert.NotEmpty(decision.EvidenceRefs);
        Assert.True(decision.Confidence > 0);
        Assert.NotNull(decision.Explanation);
        Assert.False(string.IsNullOrWhiteSpace(decision.Explanation.Why));
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
        public void PublishDecisionMade(Guid decisionId, Guid requestId, double confidence, double riskScore, ReasoningType reasoningTypeApplied)
        {
        }
    }

    private sealed class NoOpLowConfidenceDecisionFlaggedEventPublisher : ILowConfidenceDecisionFlaggedEventPublisher
    {
        public void PublishLowConfidenceDecisionFlagged(Guid decisionId, Guid correlationId, double confidence, double threshold)
        {
        }
    }

    private sealed class NoOpContextExpansionRequestedEventPublisher : IContextExpansionRequestedEventPublisher
    {
        public void PublishContextExpansionRequested(Guid requestId, ReasoningContextScope originalScope, ReasoningContextScope expandedScope)
        {
        }
    }
}
