using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

public interface IKnowledgeClient
{
    Task UpdateAsync(
        Guid nodeId,
        KnowledgeNodeType nodeType,
        string content,
        string[] domainTags,
        string[] evidenceRefs,
        CancellationToken cancellationToken = default);
}
