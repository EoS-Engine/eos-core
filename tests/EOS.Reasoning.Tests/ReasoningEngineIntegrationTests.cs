using EOS.AIProvider;
using EOS.Contracts;

namespace EOS.Reasoning.Tests;

public class ReasoningEngineIntegrationTests
{
    [Fact]
    public async Task ReasonAsync_ExplainSOLIDPrinciples_ReturnsADecisionWithNonEmptyEvidenceConfidenceAndExplanation()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
        var aiProviderClient = new OllamaProviderAdapter(httpClient, "qwen2.5-coder:7b", maxTokens: 4096, temperature: 0.2);
        var engine = new ReasoningEngine(aiProviderClient);
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
}
