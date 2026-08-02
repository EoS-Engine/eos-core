using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Composition Root Adapter (ADR-015-001) for §19's <c>KnowledgeQualityUpdated</c> event
/// ("Quality/Metadata Manager (§10.9)", payload "node_id, quality_profile (§13.1)").
/// </summary>
public interface IKnowledgeQualityUpdatedEventPublisher
{
    void PublishKnowledgeQualityUpdated(Guid nodeId, QualityProfile qualityProfile);
}
