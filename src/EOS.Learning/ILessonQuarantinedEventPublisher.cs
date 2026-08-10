namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>LessonQuarantined</c> event (new in v1.1),
/// producer: Learning Engine — per the Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface ILessonQuarantinedEventPublisher
{
    void PublishLessonQuarantined(Guid recordId, string reason);
}
