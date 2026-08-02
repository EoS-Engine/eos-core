namespace EOS.Reasoning;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §17's <c>LowConfidenceDecisionFlagged</c> event — see
/// <see cref="IDecisionMadeEventPublisher"/> for the Composition Root Adapter Pattern rationale.
/// </summary>
public interface ILowConfidenceDecisionFlaggedEventPublisher
{
    void PublishLowConfidenceDecisionFlagged(Guid decisionId, double confidence, double threshold);
}
