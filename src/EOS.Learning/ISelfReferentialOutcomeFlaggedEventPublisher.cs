namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>SelfReferentialOutcomeFlagged</c> event (new in
/// v1.1) — payload frozen exactly as specified: "record_id, task_id".
/// </summary>
public interface ISelfReferentialOutcomeFlaggedEventPublisher
{
    void PublishSelfReferentialOutcomeFlagged(Guid recordId, Guid taskId);
}
