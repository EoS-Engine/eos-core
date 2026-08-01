namespace EOS.Knowledge;

/// <summary>
/// Composition Root Adapter (ADR-015-001) for §19's <c>KnowledgeFreshnessExpired</c> event
/// ("Freshness Manager (§17.2)", payload "node_id, freshness_score").
/// </summary>
public interface IKnowledgeFreshnessExpiredEventPublisher
{
    void PublishKnowledgeFreshnessExpired(Guid nodeId, double freshnessScore);
}
