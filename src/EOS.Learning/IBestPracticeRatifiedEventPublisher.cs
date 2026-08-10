namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>BestPracticeRatified</c> event, carried forward
/// unchanged from v1.0 — no payload shape is specified beyond the event's own name anywhere in
/// any frozen document, so this uses the minimal, non-invented shape (the promoted record's own
/// identity), matching every other Learning Engine event's own record-identity-centric pattern.
/// </summary>
public interface IBestPracticeRatifiedEventPublisher
{
    void PublishBestPracticeRatified(Guid recordId);
}
