using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

public sealed class KnowledgeClient(KnowledgeGraphStore store) : IKnowledgeClient
{
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
}
