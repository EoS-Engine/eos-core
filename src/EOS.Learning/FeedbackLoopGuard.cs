using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.5's Feedback Loop Guard — reacts to
/// <c>PlatformCapabilityPipelineAdvanced</c>, flags any downstream task whose own outcome feeds
/// back into the same record's <see cref="PipelineRecord.KnowledgeGraphRef"/>, preventing a
/// promoted-but-wrong Golden Path from "confirming itself." Real, directly-callable code — the
/// <c>PlatformCapabilityPipelineAdvanced</c> subscription itself is Composition Root wiring
/// (<c>Program.cs</c>), not owned here.
/// </summary>
public sealed class FeedbackLoopGuard(
    ITaskProvenanceQueryClient taskProvenanceQueryClient,
    ISelfReferentialOutcomeFlaggedEventPublisher selfReferentialOutcomeFlaggedEventPublisher)
{
    public async Task<int> CheckAsync(PipelineRecord record, CancellationToken cancellationToken = default)
    {
        var selfReferentialTaskIds = await taskProvenanceQueryClient.GetSelfReferentialTaskIdsAsync(
            record.KnowledgeGraphRef, cancellationToken);

        foreach (var taskId in selfReferentialTaskIds)
        {
            selfReferentialOutcomeFlaggedEventPublisher.PublishSelfReferentialOutcomeFlagged(record.RecordId, taskId);
        }

        return selfReferentialTaskIds.Count;
    }
}
