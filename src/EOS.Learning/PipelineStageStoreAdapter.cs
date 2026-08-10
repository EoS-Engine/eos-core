using EOS.Contracts;
using EOS.Knowledge;

namespace EOS.Learning;

/// <summary>
/// Real backing for <c>EOS.Knowledge</c>'s <see cref="IPipelineStageStore"/> (WP-016's interface,
/// stubbed by <c>NotYetPromotedPipelineStageStore</c> pending this WP — see that interface's own
/// documentation). WP-026 Implementation Approval Report §C: <paramref name="episodicEntryId"/>
/// is exactly the sole entry of the originating <see cref="PipelineRecord.SourceLessonIds"/>
/// (traced through <c>KnowledgeClient.ConsolidateAsync</c>: the same GUID is used for the
/// <c>KnowledgeNode.NodeId</c>, the <c>LessonLearned.EpisodicEntryId</c>, and, per §11.1's own
/// pseudocode, <c>PipelineRecord.source_lesson_ids[0]</c>). WP-026 caps promotion at
/// <c>Pattern</c>, so "reached Pattern or beyond" simplifies to an exact stage check here.
/// </summary>
public sealed class PipelineStageStoreAdapter(IPipelineRecordStore pipelineRecordStore) : IPipelineStageStore
{
    public async Task<bool> HasReachedPatternStageOrBeyondAsync(
        Guid episodicEntryId, CancellationToken cancellationToken = default)
    {
        var record = await pipelineRecordStore.GetBySourceLessonIdAsync(episodicEntryId, cancellationToken);
        return record is not null && record.Stage == PipelineStage.Pattern;
    }
}
