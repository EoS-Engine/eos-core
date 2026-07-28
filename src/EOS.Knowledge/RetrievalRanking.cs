using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §19.1's mechanical ranking formula. WP-014's
/// <c>query()</c> has no vector-similarity input available to it (see the documented
/// architectural limitation on <see cref="IKnowledgeClient.QueryAsync"/>), so the
/// vector-similarity term always evaluates to zero here; access-frequency has no tracking
/// data source anywhere in this codebase yet, so it also always evaluates to zero. Both
/// remain structurally present in the formula (and in <see cref="RankingWeights"/>'s
/// configuration) so a future WP can supply real values without changing this formula's shape.
/// Recency is computed from <see cref="KnowledgeNode.CreatedAt"/>, the only timestamp
/// <see cref="KnowledgeNode"/> carries — WP-007 deliberately never rewrites it on update.
/// </summary>
public static class RetrievalRanking
{
    public static IReadOnlyList<KnowledgeNode> Rank(
        IReadOnlyList<KnowledgeNode> candidates, RankingWeights weights, string[]? domainScope)
    {
        return candidates
            .OrderByDescending(node => Score(node, weights, domainScope))
            .ToList();
    }

    private static double Score(KnowledgeNode node, RankingWeights weights, string[]? domainScope)
    {
        const double vectorSimilarity = 0.0;
        const double accessFrequency = 0.0;
        var recency = RecencyDecay(node.CreatedAt);
        var domainMatch = DomainMatch(node.DomainTags, domainScope);

        return (weights.VectorSimilarity * vectorSimilarity)
            + (weights.Recency * recency)
            + (weights.DomainMatch * domainMatch)
            + (weights.AccessFrequency * accessFrequency);
    }

    private static double RecencyDecay(DateTimeOffset createdAt)
    {
        var age = DateTimeOffset.UtcNow - createdAt;
        return 1.0 / (1.0 + Math.Max(age.TotalDays, 0));
    }

    private static double DomainMatch(IReadOnlyCollection<string> domainTags, string[]? domainScope)
    {
        if (domainScope is not { Length: > 0 })
        {
            return 0.0;
        }

        return domainTags.Intersect(domainScope).Any() ? 1.0 : 0.0;
    }
}
