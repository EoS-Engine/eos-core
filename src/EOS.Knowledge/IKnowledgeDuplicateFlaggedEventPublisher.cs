namespace EOS.Knowledge;

/// <summary>
/// Composition Root Adapter (ADR-015-001) for §19's <c>KnowledgeDuplicateFlagged</c> event
/// ("Discovery/Reuse Engine (§18.4)", payload "node_id_a, node_id_b, similarity_source").
/// </summary>
public interface IKnowledgeDuplicateFlaggedEventPublisher
{
    void PublishKnowledgeDuplicateFlagged(Guid nodeIdA, Guid nodeIdB, string similaritySource);
}
