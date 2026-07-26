namespace EOS.KnowledgeGraph;

public sealed record KnowledgeNode(
    Guid NodeId,
    KnowledgeNodeType NodeType,
    string Content,
    string[] DomainTags,
    string[] EvidenceRefs,
    DateTimeOffset CreatedAt);
