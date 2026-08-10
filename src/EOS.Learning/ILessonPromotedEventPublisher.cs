namespace EOS.Learning;

/// <summary>
/// Constitution Part 3's <c>LessonPromoted</c> event, reused verbatim (Constitution §600:
/// "lesson_id → pattern_id"), producer: Learning Engine — per the Composition Root Adapter
/// Pattern (ADR-015-001).
/// </summary>
public interface ILessonPromotedEventPublisher
{
    void PublishLessonPromoted(Guid recordId, Guid patternRecordId);
}
