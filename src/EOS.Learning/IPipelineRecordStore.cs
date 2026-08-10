using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §7's Ownership matrix: <c>EOS.Learning</c> is the sole
/// owner of <c>PipelineRecord</c> metadata persistence (INV-1: content itself is never stored
/// here, only metadata and a <see cref="PipelineRecord.KnowledgeGraphRef"/> reference).
/// </summary>
public interface IPipelineRecordStore
{
    Task EnsureTableExistsAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(PipelineRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// §11.1's idempotency check ("<c>if PipelineRecord.exists(event.event_id): return</c>") and
    /// the basis for <see cref="EOS.Knowledge.IPipelineStageStore"/>'s adapter (§14.1's
    /// <c>source_lesson_ids</c> is set once, at creation, to <c>[event.event_id]</c>, and is
    /// never appended to within this WP's scope).
    /// </summary>
    Task<PipelineRecord?> GetBySourceLessonIdAsync(Guid episodicEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the subset of <paramref name="knowledgeGraphRefs"/> (candidate
    /// <c>KnowledgeNode.NodeId</c>s returned by <c>IKnowledgeClient.QuerySimilarAsync</c>) that
    /// have a corresponding <c>PipelineRecord</c> — used by <c>ClusterTrigger</c> to resolve
    /// <c>KnowledgeNode</c> candidates into <c>PipelineRecord</c> candidates before excluding
    /// Quarantined/Archived ones (§11.2).
    /// </summary>
    Task<IReadOnlyList<PipelineRecord>> GetByKnowledgeGraphRefsAsync(
        IEnumerable<Guid> knowledgeGraphRefs, CancellationToken cancellationToken = default);

    /// <summary>
    /// WP-027: direct lookup by <see cref="PipelineRecord.RecordId"/> — the stage-promotion
    /// pipeline (Pattern and beyond) operates on records already resolved by their own identity,
    /// not by a source Lesson or a candidate KnowledgeGraphRef set.
    /// </summary>
    Task<PipelineRecord?> GetByIdAsync(Guid recordId, CancellationToken cancellationToken = default);

    /// <summary>WP-027: <see cref="StallDetector"/>/<see cref="FitnessMonitor"/> need to enumerate records by <see cref="PipelineRecordStatus"/> (e.g. all <see cref="PipelineRecordStatus.Active"/> records to check for staleness).</summary>
    Task<IReadOnlyList<PipelineRecord>> GetByStatusAsync(PipelineRecordStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// §11.2's <c>StageEngine.promote(record, to=Pattern, evidence=guard_result)</c> — mutates
    /// the same record in place (never creates a new one), and the confidence-only-update path
    /// ("no promotion; not an error, just insufficient confidence") which updates
    /// <see cref="PipelineRecord.ConfidenceScore"/> without changing <see cref="PipelineRecord.Stage"/>.
    /// </summary>
    Task UpdateStageAsync(
        Guid recordId,
        PipelineStage stage,
        PipelineRecordStatus status,
        double confidenceScore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// WP-027: makes <see cref="PipelineRecord.ApprovalRefs"/> writable post-creation — e.g.
    /// Pattern→BestPractice's "Principal Engineer ratification, ADR-linked" (§16) records the
    /// ADR reference here.
    /// </summary>
    Task UpdateApprovalRefsAsync(Guid recordId, string[] approvalRefs, CancellationToken cancellationToken = default);

    /// <summary>
    /// WP-027: makes <see cref="PipelineRecord.RoiEvaluationRef"/> writable post-creation — set
    /// by the ROI Gate (§11.3) on every evaluation attempt, whether denied, threshold-unavailable,
    /// or (once <c>roi_minimum</c> is supplied) a real pass/fail comparison.
    /// </summary>
    Task UpdateRoiEvaluationRefAsync(Guid recordId, string roiEvaluationRef, CancellationToken cancellationToken = default);
}
