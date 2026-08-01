namespace EOS.Knowledge;

/// <summary>
/// Composition Root Adapter (ADR-015-001) for §19's <c>KnowledgeGovernanceActionApplied</c>
/// event ("Governance Manager, post-Protection-Allow", payload "node_id, action_type, new_version
/// (§12.6)").
/// </summary>
public interface IKnowledgeGovernanceActionAppliedEventPublisher
{
    void PublishKnowledgeGovernanceActionApplied(Guid nodeId, GovernanceActionType actionType, int newVersion);
}
