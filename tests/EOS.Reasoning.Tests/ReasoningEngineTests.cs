using EOS.Contracts;
using EOS.Reasoning;
using EOS.SDK;

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReasonAsync_ThrowsInvalidGoal_WhenGoalIsEmptyOrWhitespace(string goal)
    {
        var engine = new ReasoningEngine(new StubAIProviderClient(succeed: true, output: "unused"));

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(
            () => engine.ReasonAsync(CreateRequest(goal)));

        Assert.Equal(ReasoningFailureMode.InvalidGoal, exception.FailureMode);
    }

    [Fact]
    public async Task ReasonAsync_ThrowsInternalError_WhenInferenceFails()
    {
        var engine = new ReasoningEngine(new StubAIProviderClient(succeed: false, output: null));

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(
            () => engine.ReasonAsync(CreateRequest("explain the SOLID principles")));

        Assert.Equal(ReasoningFailureMode.InternalError, exception.FailureMode);
    }

    [Fact]
    public async Task ReasonAsync_ReturnsASingleDecision_WithNonEmptyEvidenceConfidenceAndExplanation()
    {
        var engine = new ReasoningEngine(new StubAIProviderClient(succeed: true, output: "SOLID stands for..."));
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
