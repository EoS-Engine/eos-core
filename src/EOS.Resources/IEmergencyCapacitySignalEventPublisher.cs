using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>EmergencyCapacitySignal</c> event (producer:
/// Capacity Manager, on Emergency threshold, §17.4), per the Composition Root Adapter Pattern
/// (ADR-015-001). Informational only — Protection Layer retains sole Emergency Shutdown
/// authority (§17.4, Protection-Layer-Specification-v1.0 FR-P9).
/// </summary>
public interface IEmergencyCapacitySignalEventPublisher
{
    void PublishEmergencyCapacitySignal(ResourceType resourceType, double measuredValue);
}
