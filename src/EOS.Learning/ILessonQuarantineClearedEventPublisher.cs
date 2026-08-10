namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>LessonQuarantineCleared</c> event (new in v1.1) —
/// payload frozen exactly as specified: "record_id, clearing_role, justification".
/// </summary>
public interface ILessonQuarantineClearedEventPublisher
{
    void PublishLessonQuarantineCleared(Guid recordId, string clearingRole, string justification);
}
