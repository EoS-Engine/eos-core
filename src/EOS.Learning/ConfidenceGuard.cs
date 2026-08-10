namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.2's <c>ConfidenceGuard.assess(similarity_results,
/// record.trust_score)</c> — the pseudocode passes <c>record.trust_score</c> directly, meaning
/// trust is already resolved by the time clustering runs (populated once, at
/// <see cref="Ingestion"/> time, via <c>IReasoningEngineClient.GetTrustSignalAsync</c>, per
/// §24.4: "trust_score... is populated from get_trust_signal()... and factored into
/// ConfidenceGuard.assess()"). This class therefore performs the pure combination only — no
/// I/O, no dependency on <c>IReasoningEngineClient</c> — matching the locked decision
/// "Overall confidence = comparison confidence × trust score".
/// </summary>
public sealed class ConfidenceGuard
{
    public double Assess(double comparisonConfidence, double trustScore) => comparisonConfidence * trustScore;
}
