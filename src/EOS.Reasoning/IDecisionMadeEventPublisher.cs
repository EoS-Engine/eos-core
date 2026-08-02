using EOS.Contracts;

namespace EOS.Reasoning;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §17's <c>DecisionMade</c> event, per the Composition Root
/// Adapter Pattern (ADR-015-001): <c>EOS.Reasoning</c> defines this small, BCL/local-typed
/// interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter bridging to
/// <c>EventEnvelope</c>/<c>EventMediator</c> (<c>EOS.Contracts</c>/<c>EOS.Orchestrator</c>),
/// which <c>EOS.Reasoning</c> has no legal dependency path to reach directly.
/// </summary>
public interface IDecisionMadeEventPublisher
{
    void PublishDecisionMade(Guid decisionId, Guid requestId, double confidence, double riskScore, ReasoningType reasoningTypeApplied);
}
