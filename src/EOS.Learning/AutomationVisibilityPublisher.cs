using EOS.Knowledge;
using EOS.KnowledgeGraph;

namespace EOS.Learning;

/// <summary>
/// WP-027 Decision 2 (locked): at <c>GoldenPath -&gt; Automation</c>, after the stage transition
/// is persisted, re-types the underlying <see cref="KnowledgeNode"/> to
/// <see cref="KnowledgeNodeType.Pattern"/> via the existing, already-legal
/// <see cref="IKnowledgeClient.UpdateAsync"/> — the only mechanism by which the existing,
/// unmodified <c>EOS.Planner.TaskGraphBuilder</c> (which only ever queries
/// <see cref="KnowledgeNodeType.Pattern"/> nodes) can discover a promoted Automation.
/// Content/DomainTags/EvidenceRefs/Metadata are preserved unchanged — only <c>NodeType</c> is
/// modified. No <c>EOS.Planner</c> production code is touched.
/// </summary>
public sealed class AutomationVisibilityPublisher(IKnowledgeClient knowledgeClient)
{
    public async Task MakeDiscoverableAsync(Guid knowledgeGraphRef, CancellationToken cancellationToken = default)
    {
        var allNodes = await knowledgeClient.QueryAsync(type: null, domainTags: null, range: null, cancellationToken);
        var node = allNodes.FirstOrDefault(candidate => candidate.NodeId == knowledgeGraphRef)
            ?? throw new InvalidOperationException($"KnowledgeNode '{knowledgeGraphRef}' does not exist.");

        await knowledgeClient.UpdateAsync(
            node.NodeId,
            KnowledgeNodeType.Pattern,
            node.Content,
            node.DomainTags,
            node.EvidenceRefs,
            node.Metadata,
            cancellationToken);
    }
}
