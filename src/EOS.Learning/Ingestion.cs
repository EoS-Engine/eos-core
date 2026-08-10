using EOS.Contracts;
using EOS.Knowledge;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.1's <c>on LessonLearned(event)</c> — idempotency,
/// the Ingestion Rate Guard, <see cref="PipelineRecord"/> creation, and delegation into
/// <see cref="ClusterTrigger"/> for a normally-ingested (non-Quarantined) Lesson.
///
/// Resolving the originating Lesson's <see cref="KnowledgeNode.DomainTags"/> uses
/// <see cref="IKnowledgeClient.QueryAsync"/> (an existing, unmodified public method) rather than
/// a new "get node by id" addition to <c>EOS.Knowledge</c> — <c>LessonLearnedPayload</c> carries
/// only <c>EpisodicEntryId</c>/<c>Source</c>, not domain tags, so this is the only available,
/// boundary-respecting path to them.
/// </summary>
public sealed class Ingestion(
    IKnowledgeClient knowledgeClient,
    IReasoningEngineClient reasoningEngineClient,
    IPipelineRecordStore pipelineRecordStore,
    IngestionRateGuard ingestionRateGuard,
    ClusterTrigger clusterTrigger,
    ILessonQuarantinedEventPublisher lessonQuarantinedEventPublisher)
{
    public async Task OnLessonLearnedAsync(Guid episodicEntryId, string source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("source must not be empty.", nameof(source));
        }

        // §11.1 idempotency: "if PipelineRecord.exists(event.event_id): return".
        if (await pipelineRecordStore.GetBySourceLessonIdAsync(episodicEntryId, cancellationToken) is not null)
        {
            return;
        }

        var domainTags = await ResolveDomainTagsAsync(episodicEntryId, cancellationToken);

        if (await ingestionRateGuard.ExceedsThresholdAsync(source, cancellationToken))
        {
            var quarantined = NewRecord(episodicEntryId, domainTags, trustScore: 0.5, PipelineRecordStatus.Quarantined);
            await pipelineRecordStore.InsertAsync(quarantined, cancellationToken);
            lessonQuarantinedEventPublisher.PublishLessonQuarantined(quarantined.RecordId, "ingestion rate anomaly");
            return;
        }

        var trustScore = await ResolveTrustScoreAsync(source, cancellationToken);
        var record = NewRecord(episodicEntryId, domainTags, trustScore, PipelineRecordStatus.Active);
        await pipelineRecordStore.InsertAsync(record, cancellationToken);

        await clusterTrigger.EvaluateAsync(record, cancellationToken);
    }

    private static PipelineRecord NewRecord(
        Guid episodicEntryId, string[] domainTags, double trustScore, PipelineRecordStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new PipelineRecord(
            RecordId: Guid.NewGuid(),
            Stage: PipelineStage.Lesson,
            KnowledgeGraphRef: episodicEntryId,
            SourceLessonIds: [episodicEntryId],
            DomainTags: domainTags,
            CreatedAt: now,
            LastAdvancedAt: now,
            ApprovalRefs: [],
            RoiEvaluationRef: null,
            TrustScore: trustScore,
            ConfidenceScore: 0.0,
            Status: status);
    }

    private async Task<string[]> ResolveDomainTagsAsync(Guid episodicEntryId, CancellationToken cancellationToken)
    {
        var episodicNodes = await knowledgeClient.QueryAsync(MemoryType.Episodic, null, null, cancellationToken);
        return episodicNodes.FirstOrDefault(node => node.NodeId == episodicEntryId)?.DomainTags ?? [];
    }

    private async Task<double> ResolveTrustScoreAsync(string source, CancellationToken cancellationToken)
    {
        try
        {
            var trustSignal = await reasoningEngineClient.GetTrustSignalAsync(source, cancellationToken);
            return trustSignal.Score;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // §14.2 failure contract: unavailability falls back to neutral trust — never fails
            // open to full trust.
            return 0.5;
        }
    }
}
