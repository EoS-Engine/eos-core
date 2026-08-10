namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>LessonStalled</c> event, carried forward
/// unchanged from v1.0 — see <see cref="IBestPracticeRatifiedEventPublisher"/> for why the
/// payload is the minimal, non-invented record-identity shape.
/// </summary>
public interface ILessonStalledEventPublisher
{
    void PublishLessonStalled(Guid recordId);
}
