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
}
