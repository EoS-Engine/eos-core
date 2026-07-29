using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

public sealed class KnowledgeClient(KnowledgeGraphStore store, RankingWeights rankingWeights) : IKnowledgeClient
{
    private static readonly IReadOnlyList<KnowledgeNodeType> AllNodeTypes =
    [
        KnowledgeNodeType.Fact,
        KnowledgeNodeType.Lesson,
        KnowledgeNodeType.Pattern,
        KnowledgeNodeType.Decision,
        KnowledgeNodeType.Risk,
    ];

    public Task UpdateAsync(
        Guid nodeId,
        KnowledgeNodeType nodeType,
        string content,
        string[] domainTags,
        string[] evidenceRefs,
        CancellationToken cancellationToken = default)
    {
        var node = new KnowledgeNode(
            NodeId: nodeId,
            NodeType: nodeType,
            Content: content,
            DomainTags: domainTags,
            EvidenceRefs: evidenceRefs,
            CreatedAt: DateTimeOffset.UtcNow);

        return store.UpsertAsync(node, cancellationToken);
    }

    public async Task<IEnumerable<KnowledgeNode>> QueryAsync(
        MemoryType? type,
        string[]? domainTags,
        DateRange? range,
        CancellationToken cancellationToken = default)
    {
        if (type is MemoryType.Project && domainTags is not { Length: > 0 })
        {
            throw new ArgumentException(
                "Project memory is a domain_tags-filtered view (Memory-Management-Specification-v1.0 §10.6) " +
                "and requires at least one domain tag.",
                nameof(domainTags));
        }

        var nodeTypes = ResolveNodeTypes(type);

        IReadOnlyList<KnowledgeNode> candidates =
            await store.QueryAsync(nodeTypes, range?.From, range?.To, cancellationToken);

        if (domainTags is { Length: > 0 })
        {
            candidates = candidates
                .Where(node => node.DomainTags.Intersect(domainTags).Any())
                .ToList();
        }

        return RetrievalRanking.Rank(candidates, rankingWeights, domainTags);
    }

    public async Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(
        Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await store.GetByIdAsync(nodeId, cancellationToken);
        if (node is null)
        {
            throw new ArgumentException($"'{nodeId}' does not resolve to an existing node.", nameof(nodeId));
        }

        var candidates = (await store.QueryAsync([node.NodeType], null, null, cancellationToken))
            .Where(candidate => candidate.NodeId != nodeId)
            .ToList();

        return RetrievalRanking.Rank(candidates, rankingWeights, node.DomainTags);
    }

    private static IReadOnlyList<KnowledgeNodeType> ResolveNodeTypes(MemoryType? type)
    {
        return type switch
        {
            null => AllNodeTypes,
            MemoryType.Episodic => [KnowledgeNodeType.Lesson],
            MemoryType.Semantic => [KnowledgeNodeType.Fact, KnowledgeNodeType.Pattern],
            MemoryType.LongTerm =>
                [KnowledgeNodeType.Fact, KnowledgeNodeType.Pattern, KnowledgeNodeType.Decision, KnowledgeNodeType.Risk],
            MemoryType.Project => AllNodeTypes,
            MemoryType.Working or MemoryType.ShortTerm or MemoryType.Session => throw new NotSupportedException(
                $"MemoryType.{type} is not queryable through IKnowledgeClient.QueryAsync() — it is not " +
                "represented as a KnowledgeNode (Memory-Management-Specification-v1.0 §10.1/§10.2/§10.7). " +
                "Its retrieval surface belongs to future runtime components that own ephemeral execution " +
                "state, not to the Knowledge query API. Documented architectural limitation, WP-014."),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unrecognized MemoryType."),
        };
    }
}
