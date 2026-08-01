namespace EOS.Knowledge;

/// <summary>
/// Composition Root Adapter (ADR-015-001) for §19's <c>KnowledgeGovernanceActionRequested</c>
/// event ("Governance Manager (§16)", consumer "Protection Layer (<c>IProtectionClient.validate()</c>)",
/// payload "node_id, action_type (§16.4), requested_by").
/// </summary>
public interface IKnowledgeGovernanceActionRequestedEventPublisher
{
    void PublishKnowledgeGovernanceActionRequested(Guid nodeId, GovernanceActionType actionType, string requestedBy);
}
