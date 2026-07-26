using EOS.Contracts;
using EOS.SDK;

namespace EOS.Reasoning;

public sealed class ReasoningEngine(IAIProviderClient aiProviderClient) : IReasoningEngineClient
{
    private const double FixedConfidence = 0.5;
    private const double FixedRiskScore = 0;

    public async Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            throw new ReasoningFailedException(ReasoningFailureMode.InvalidGoal, "Goal must not be empty.");
        }

        var inferenceRequest = new InferenceRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: request.CorrelationId,
            CapabilityRequired: "Chat",
            Payload: request.Goal,
            ContextPayloadRef: null,
            TokenBudgetEstimate: 2048,
            Priority: 0,
            Caller: "EOS.Reasoning");

        var inferenceResult = await aiProviderClient.InferAsync(inferenceRequest, cancellationToken);

        if (!inferenceResult.Success || string.IsNullOrWhiteSpace(inferenceResult.Output))
        {
            throw new ReasoningFailedException(
                ReasoningFailureMode.InternalError,
                inferenceResult.ErrorMessage ?? "AI Provider returned no usable output.");
        }

        string[] evidenceRefs = [$"inference:{inferenceRequest.RequestId}"];

        var explanation = new Explanation(
            Why: $"The AI Provider's inference output was selected as the answer to the stated goal: \"{request.Goal}\"",
            EvidenceUsed: evidenceRefs,
            Assumptions: [$"The single inference call from provider model '{inferenceResult.Model}' is treated as sufficient without independent corroboration."],
            AlternativesRejected: [],
            ConfidenceRationale: "Confidence reflects a single real, unweighted, uncorroborated inference source (no Context Assembly, no multi-hypothesis comparison, no trust signal available in this minimal pipeline).",
            Risks: ["This decision is based on exactly one uncorroborated model inference and has not been validated against retrieved knowledge or historical outcomes."]);

        var decision = new Decision(
            DecisionId: Guid.NewGuid(),
            RequestId: request.RequestId,
            ReasoningTypeApplied: ReasoningType.EngineeringReasoning,
            SelectedHypothesis: inferenceResult.Output,
            RejectedHypotheses: [],
            EvidenceRefs: evidenceRefs,
            Confidence: FixedConfidence,
            Explanation: explanation,
            TradeOffs: "No alternative hypotheses were generated in this minimal pipeline; trade-off analysis does not apply.",
            RiskScore: FixedRiskScore,
            Reproducible: false,
            OccurredAt: DateTimeOffset.UtcNow);

        return [decision];
    }
}
