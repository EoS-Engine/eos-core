using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §20.1 (WP-017's slice: taxonomy classification and
/// relationship navigation only — <c>get_quality()</c>/<c>search()</c> are WP-018). Every
/// mutation routes through <see cref="IKnowledgeClient.UpdateAsync"/>, never a direct store
/// write (FR-KM1; §20.1's own <c>classify()</c> responsibility text: "read/write via Memory's
/// <c>IKnowledgeClient.update()</c>, never a direct store write").
/// </summary>
public interface IKnowledgeManagementClient
{
    /// <summary>
    /// Assigns a node's taxonomy classification (§11). Throws <see cref="ArgumentException"/>
    /// if <paramref name="nodeId"/> does not resolve to an existing node.
    /// </summary>
    Task ClassifyAsync(Guid nodeId, TaxonomyClassification taxonomy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a node's taxonomy classification (§11), or <see langword="null"/> if the node
    /// has never been classified.
    /// </summary>
    Task<TaxonomyClassification?> GetClassificationAsync(Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a typed Relationship edge (§14) from <paramref name="sourceNodeId"/>. Validated
    /// against §14's Ontology constraints (<see cref="OntologyValidator"/>) before persisting —
    /// throws <see cref="ArgumentException"/> if the edge violates one, or if
    /// <paramref name="sourceNodeId"/> does not resolve to an existing node. Target-node
    /// existence is enforced only where §14's table states it (currently
    /// <see cref="RelationshipType.Requires"/> only) — <see cref="OntologyValidator"/>'s own
    /// documentation lists exactly which relationship types require it.
    /// </summary>
    Task AddRelationshipAsync(Guid sourceNodeId, RelationshipEdge edge, CancellationToken cancellationToken = default);

    /// <summary>
    /// §20.1's <c>navigate_relationships()</c> — Relationship Navigation search intent (§15.1),
    /// read-only. Returns every edge on <paramref name="nodeId"/>, optionally filtered to a
    /// single <see cref="RelationshipType"/>.
    /// </summary>
    Task<IReadOnlyList<RelationshipEdge>> NavigateRelationshipsAsync(
        Guid nodeId, RelationshipType? type = null, CancellationToken cancellationToken = default);
}
