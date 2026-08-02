using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §20.1's <c>search(SearchRequest request)</c>
/// parameter. <see cref="Type"/>/<see cref="DomainTags"/>/<see cref="Range"/> are passed through,
/// unchanged, to <see cref="IKnowledgeClient.QueryAsync"/> (FR-KM3: Memory's own retrieval is
/// never altered). <see cref="RelationshipContextNodeId"/> feeds §15.7's
/// <c>relationship_relevance(item, request.relationship_context)</c> term.
/// </summary>
public sealed record SearchRequest
{
    public MemoryType? Type { get; init; }

    public string[]? DomainTags { get; init; }

    public DateRange? Range { get; init; }

    public Guid? RelationshipContextNodeId { get; init; }
}
