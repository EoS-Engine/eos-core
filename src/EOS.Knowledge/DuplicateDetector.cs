using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §18.3/§18.4's Duplicate Detection — "a specific
/// application of Similarity Detection... flagging two knowledge objects as likely duplicates —
/// flagged, never auto-merged." Structural gate per §18.3's exact signal list ("shared taxonomy
/// tag, shared <c>domain_tags</c>, shared Relationship neighbors"); candidates passing the
/// structural gate are additionally checked via <see cref="ICompareProvider"/> (WP-018's
/// roadmap-authorized structural stub for Reasoning Engine's <c>compare()</c>) so the returned
/// <see cref="DuplicateCandidate.SimilaritySource"/> is accurate once WP-020 supplies a real
/// implementation, without WP-018 itself performing semantic comparison (§18.3's boundary).
/// </summary>
public sealed class DuplicateDetector(KnowledgeGraphStore store, ICompareProvider compareProvider)
{
    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await store.GetByIdAsync(nodeId, cancellationToken)
            ?? throw new ArgumentException($"'{nodeId}' does not resolve to an existing node.", nameof(nodeId));

        var candidates = await store.QueryAsync([node.NodeType], null, null, cancellationToken);
        var relationshipNeighborIds = (node.Metadata?.Relationships ?? [])
            .Select(edge => edge.TargetNodeId)
            .ToHashSet();

        var results = new List<DuplicateCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.NodeId == nodeId)
            {
                continue;
            }

            var sharedTaxonomy = node.Metadata?.Taxonomy is not null
                && node.Metadata.Taxonomy == candidate.Metadata?.Taxonomy;
            var sharedDomainTag = node.DomainTags.Intersect(candidate.DomainTags).Any();
            var sharedRelationshipNeighbor = relationshipNeighborIds.Contains(candidate.NodeId)
                || (candidate.Metadata?.Relationships ?? []).Any(edge => edge.TargetNodeId == nodeId);

            if (!((sharedTaxonomy && sharedDomainTag) || sharedRelationshipNeighbor))
            {
                continue;
            }

            var semanticallySimilar = await compareProvider.AreSimilarAsync(
                node.Content, candidate.Content, cancellationToken);
            results.Add(new DuplicateCandidate(
                candidate.NodeId, semanticallySimilar ? "Reasoning compare()" : "structural"));
        }

        return results;
    }
}
