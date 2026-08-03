using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>ResourceQuotaExhausted</c> event (producer:
/// Quota Manager, §10.4), per the Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface IResourceQuotaExhaustedEventPublisher
{
    void PublishResourceQuotaExhausted(ResourceClass resourceClass, ResourceType resourceType);
}
