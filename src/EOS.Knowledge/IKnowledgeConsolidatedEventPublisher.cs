namespace EOS.Knowledge;

/// <summary>
/// Composition Root Adapter (ADR-015-001) for §19's <c>KnowledgeConsolidated</c> event
/// ("Discovery/Reuse Engine, post-Governance-approval (§18.5)", payload "canonical_node_id,
/// superseded_node_ids[]"). Interface defined for event ownership completeness; intentionally
/// unwired this WP. §18.5's Knowledge Consolidation is explicitly composed from
/// <see cref="IKnowledgeManagementClient.RequestGovernanceActionAsync"/> (marking the
/// non-canonical node <see cref="EOS.KnowledgeGraph.KnowledgeLifecycleState.Retirement"/>) plus
/// WP-017's <see cref="IKnowledgeManagementClient.AddRelationshipAsync"/> (recording a
/// <see cref="EOS.KnowledgeGraph.RelationshipType.Replaces"/> edge) — §20.1's own text confirms
/// <c>find_duplicates()</c> "flags only, never merges (§18.5 requires a separate governance
/// action)," so consolidation is caller-composed orchestration of two already-existing methods,
/// not a new mechanism this WP builds. Publishing this event is therefore that caller's
/// responsibility once such an orchestrating consumer exists (no such consumer is in scope yet).
/// </summary>
public interface IKnowledgeConsolidatedEventPublisher
{
    void PublishKnowledgeConsolidated(Guid canonicalNodeId, Guid[] supersededNodeIds);
}
