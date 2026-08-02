using EOS.Contracts;

namespace EOS.Reasoning;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §17's <c>ContextExpansionRequested</c> event — see
/// <see cref="IDecisionMadeEventPublisher"/> for the Composition Root Adapter Pattern rationale.
/// </summary>
public interface IContextExpansionRequestedEventPublisher
{
    void PublishContextExpansionRequested(Guid requestId, ReasoningContextScope originalScope, ReasoningContextScope expandedScope);
}
