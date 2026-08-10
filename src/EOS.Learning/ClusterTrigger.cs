using EOS.Contracts;
using EOS.Knowledge;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.2's <c>ClusterTrigger.evaluate(record)</c>. All
/// semantic judgment is delegated to <see cref="IReasoningEngineClient"/> (INV-5) — this class
/// only resolves candidates, filters Quarantined/Archived ones (WP-026 Implementation Approval
/// Report §A/§B — <c>CompareAsync</c> itself enforces this precondition by throwing, so
/// filtering must happen here, entirely within <c>EOS.Learning</c>'s own ownership of
/// <see cref="PipelineRecord"/>), and applies the promotion rule.
/// </summary>
public sealed class ClusterTrigger(
    IKnowledgeClient knowledgeClient,
    IReasoningEngineClient reasoningEngineClient,
    IPipelineRecordStore pipelineRecordStore,
    ConfidenceGuard confidenceGuard,
    ILessonPromotedEventPublisher lessonPromotedEventPublisher,
    double clusteringConfidenceMinimum)
{
    public async Task EvaluateAsync(PipelineRecord record, CancellationToken cancellationToken = default)
    {
        // §16, WP-026 scope: an already-Pattern-or-beyond record never re-evaluates (locked
        // decision: "Repeated promotion of an already-Pattern record = NO-OP").
        if (record.Stage != PipelineStage.Lesson)
        {
            return;
        }

        var similarNodes = await knowledgeClient.QuerySimilarAsync(record.KnowledgeGraphRef, cancellationToken);
        var candidateRefs = similarNodes.Select(node => node.NodeId).ToArray();
        var resolvedCandidates = await pipelineRecordStore.GetByKnowledgeGraphRefsAsync(candidateRefs, cancellationToken);
        var filteredCandidates = resolvedCandidates
            .Where(candidate => candidate.Status is not (PipelineRecordStatus.Quarantined or PipelineRecordStatus.Archived))
            .ToArray();

        ConfidenceGuardResult compareResult;
        try
        {
            compareResult = await reasoningEngineClient.CompareAsync(record, filteredCandidates, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // §14.1 failure contract: on timeout/unavailability, treat identically to
            // "confidence below threshold" (§11.2) — never an implicit pass.
            await pipelineRecordStore.UpdateStageAsync(
                record.RecordId, record.Stage, record.Status, confidenceScore: 0.0, cancellationToken);
            return;
        }

        var overallConfidence = confidenceGuard.Assess(compareResult.Confidence, record.TrustScore);

        if (overallConfidence < clusteringConfidenceMinimum)
        {
            // §11.2: "no promotion; not an error, just insufficient confidence".
            await pipelineRecordStore.UpdateStageAsync(
                record.RecordId, record.Stage, record.Status, overallConfidence, cancellationToken);
            return;
        }

        if (compareResult.AcceptedMatches.Length < 3)
        {
            await pipelineRecordStore.UpdateStageAsync(
                record.RecordId, record.Stage, record.Status, overallConfidence, cancellationToken);
            return;
        }

        await pipelineRecordStore.UpdateStageAsync(
            record.RecordId, PipelineStage.Pattern, PipelineRecordStatus.Active, overallConfidence, cancellationToken);
        lessonPromotedEventPublisher.PublishLessonPromoted(record.RecordId, record.RecordId);
    }
}
