using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>ResourceThresholdCrossed</c> event
/// (producer: Capacity Manager, §17), per the Composition Root Adapter Pattern (ADR-015-001):
/// <c>EOS.Resources</c> defines this small interface; <c>EOS.Runner</c>'s <c>Program.cs</c>
/// supplies the concrete adapter bridging to <c>EventEnvelope</c>/<c>EventMediator</c>
/// (<c>EOS.Contracts</c>/<c>EOS.Orchestrator</c>), which <c>EOS.Resources</c> has no legal
/// dependency path to reach directly.
/// </summary>
public interface IResourceThresholdCrossedEventPublisher
{
    void PublishResourceThresholdCrossed(ResourceType resourceType, CapacityTier tier);
}
