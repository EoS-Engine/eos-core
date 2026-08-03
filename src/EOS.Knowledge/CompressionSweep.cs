using EOS.Contracts;
using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §17.2's Compression algorithm. Real, directly-callable,
/// fully-functional code — not a stub — but its *invocation cadence* (§17.2 names Sprint-cycle,
/// Constitution §0.12.1) is out of this WP's scope: no scheduler/timer/host exists anywhere in
/// this codebase yet (a pre-existing, disclosed condition this WP neither introduces nor is
/// required to close), mirroring <c>KnowledgeClient.ConsolidateAsync</c> (WP-015), which is
/// likewise real, tested, and callable without an automatic cadence of its own beyond the two
/// explicit <c>EventMediator</c> triggers WP-015 added.
///
/// Eligibility (§17.1) combines three sub-criteria, each delegated to its own Composition Root
/// Adapter interface (ADR-015-001) — never a hardcoded business decision inside this class:
/// <list type="number">
/// <item>The entry's corresponding <c>PipelineRecord</c> has reached <c>Pattern</c> stage or
/// beyond (<see cref="IPipelineStageStore"/>).</item>
/// <item>The entry has not been read recently (<see cref="IReadRecencyTracker"/>).</item>
/// <item>No legal/compliance retention hold is set (<see cref="IRetentionHoldPolicy"/>).</item>
/// </list>
///
/// WP-022: requests a Background Task Controller slot (§10.6, Resource-Management-
/// Specification-v1.0 §15) before running, per the roadmap's own Expected Deliverable —
/// "WP-016's Compression sweep... now request a slot before running, and are correctly deferred
/// under simulated contention." Background Maintenance is this sweep's own §16 resource class.
/// </summary>
public sealed class CompressionSweep(
    KnowledgeGraphStore store,
    ArchivedContentStore archivedContentStore,
    IPipelineStageStore pipelineStageStore,
    IReadRecencyTracker readRecencyTracker,
    IRetentionHoldPolicy retentionHoldPolicy,
    ISummarizer summarizer,
    IBackgroundSlotRequester backgroundSlotRequester,
    IMemoryCompressedEventPublisher? memoryCompressedEventPublisher = null)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var jobId = $"compression-sweep-{Guid.NewGuid()}";
        if (!backgroundSlotRequester.RequestSlot(jobId, ResourceClass.BackgroundMaintenance))
        {
            return 0;
        }

        var eligibleEntries = await GetEligibleForCompressionAsync(cancellationToken);

        var compressedCount = 0;

        foreach (var entry in eligibleEntries)
        {
            var originalContent = entry.Content;
            var archiveId = await archivedContentStore.ArchiveAsync(entry.NodeId, originalContent, cancellationToken);
            var summary = await summarizer.SummarizeAsync(originalContent, cancellationToken);

            await store.ReplaceContentAsync(entry.NodeId, summary, cancellationToken);

            memoryCompressedEventPublisher?.PublishMemoryCompressed(
                entry.NodeId, originalContent.Length, summary.Length);

            compressedCount++;
            _ = archiveId;
        }

        return compressedCount;
    }

    /// <summary>
    /// Memory-Management-Specification-v1.0 §17.2's <c>EpisodicMemory.eligible_for_compression()</c>
    /// — structural alignment with the specification's own named enumeration, not a behavior
    /// change from the eligibility logic previously inlined into <see cref="RunAsync"/>.
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeNode>> GetEligibleForCompressionAsync(CancellationToken cancellationToken)
    {
        var episodicEntries = await store.QueryAsync(
            [KnowledgeNodeType.Lesson], createdFrom: null, createdTo: null, cancellationToken);

        var eligible = new List<KnowledgeNode>();
        foreach (var entry in episodicEntries)
        {
            if (await IsEligibleForCompressionAsync(entry, cancellationToken))
            {
                eligible.Add(entry);
            }
        }

        return eligible;
    }

    private async Task<bool> IsEligibleForCompressionAsync(KnowledgeNode entry, CancellationToken cancellationToken)
    {
        var reachedPatternStageOrBeyond =
            await pipelineStageStore.HasReachedPatternStageOrBeyondAsync(entry.NodeId, cancellationToken);
        var readRecently = await readRecencyTracker.WasReadRecentlyAsync(entry.NodeId, cancellationToken);
        var hasActiveHold = await retentionHoldPolicy.HasActiveHoldAsync(entry.NodeId, cancellationToken);

        return reachedPatternStageOrBeyond && !readRecently && !hasActiveHold;
    }
}
