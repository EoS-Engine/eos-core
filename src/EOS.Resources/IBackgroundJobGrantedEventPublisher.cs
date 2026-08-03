using EOS.Contracts;

namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>BackgroundJobGranted</c> event (producer:
/// Background Task Controller, §15.1), per the Composition Root Adapter Pattern (ADR-015-001) —
/// same rationale as <see cref="IResourceThresholdCrossedEventPublisher"/> (WP-021).
/// </summary>
public interface IBackgroundJobGrantedEventPublisher
{
    void PublishBackgroundJobGranted(string jobId, ResourceClass resourceClass);
}
