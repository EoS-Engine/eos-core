namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §9's <c>TransitionRecord</c> — <c>TransitionId</c> (this
/// record's own identity) and <c>RecordId</c> (the owning <see cref="PipelineRecord.RecordId"/>)
/// are structurally necessary additions for persistence/lookup, not new domain semantics; every
/// other field is exactly the spec's own listed shape. <see cref="IntegrityHash"/> is WP-027's
/// locked SHA-256 self-contained hash (no chaining) over the canonical UTF-8 representation of
/// <see cref="RecordId"/>, <see cref="FromStage"/>, <see cref="ToStage"/>, <see cref="TriggeredBy"/>,
/// <see cref="EvidenceRefs"/>, <see cref="OccurredAt"/>.
/// </summary>
public sealed record TransitionRecord(
    Guid TransitionId,
    Guid RecordId,
    PipelineStage FromStage,
    PipelineStage ToStage,
    string TriggeredBy,
    string[] EvidenceRefs,
    string IntegrityHash,
    DateTimeOffset OccurredAt);
