namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §9's <c>PipelineRecord</c> — consumed, not owned, by
/// <c>EOS.Reasoning</c> (<c>compare()</c>/<c>get_trust_signal()</c>'s subject/candidate shape,
/// §14.1/§14.2). <see cref="KnowledgeGraphRef"/> and <see cref="SourceLessonIds"/> are
/// <see cref="Guid"/> references to <c>KnowledgeNode</c>s (<c>EOS.Knowledge</c>), never resolved
/// directly here — <c>EOS.Reasoning</c> has no dependency path to <c>EOS.Knowledge</c>
/// (Constitution Part 1 §1.2).
/// </summary>
public sealed record PipelineRecord(
    Guid RecordId,
    PipelineStage Stage,
    Guid KnowledgeGraphRef,
    Guid[] SourceLessonIds,
    string[] DomainTags,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAdvancedAt,
    string[] ApprovalRefs,
    string? RoiEvaluationRef,
    double TrustScore,
    double ConfidenceScore,
    PipelineRecordStatus Status);
