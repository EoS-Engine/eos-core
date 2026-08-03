namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §20's <c>BackgroundJobDeferred</c> event (producer:
/// Background Task Controller), per the Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface IBackgroundJobDeferredEventPublisher
{
    void PublishBackgroundJobDeferred(string jobId, string reason);
}
