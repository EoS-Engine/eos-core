using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>ResourceRecovered</c> event (producer:
/// Capacity Manager, §19.5: "After a Critical/Emergency threshold crossing... resolves (measured
/// load returns below Warning)"), per the Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface IResourceRecoveredEventPublisher
{
    void PublishResourceRecovered(ResourceType resourceType);
}
