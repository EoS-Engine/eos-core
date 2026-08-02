using EOS.Contracts;
using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §20.1 — WP-017 shipped <c>classify()</c>/
/// <c>navigate_relationships()</c>; WP-018 adds <c>get_quality()</c>/<c>search()</c>/
/// <c>request_governance_action()</c>/<c>find_duplicates()</c>, per the Traceability Matrix's
/// exact method split. Every mutation routes through <see cref="IKnowledgeClient.UpdateAsync"/>,
/// never a direct store write (FR-KM1; §20.1's own <c>classify()</c> responsibility text:
/// "read/write via Memory's <c>IKnowledgeClient.update()</c>, never a direct store write").
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

    /// <summary>
    /// §20.1's <c>get_quality()</c> — reads the aggregated <see cref="QualityProfile"/> (§13.1).
    /// <see cref="QualityProfile.Completeness"/> and <see cref="QualityProfile.Freshness"/> are
    /// recomputed live (the two attributes Knowledge Management itself owns, §13); every other
    /// attribute is the last value recorded from its owning subsystem (FR-KM9) — never
    /// recomputed here. The freshly-computed profile is persisted back via
    /// <see cref="IKnowledgeClient.UpdateAsync"/> and publishes <c>KnowledgeQualityUpdated</c>
    /// on every call; <c>KnowledgeFreshnessExpired</c> publishes only on the transition into an
    /// expired state, not on every subsequent call while a node remains stale. Returns
    /// <see langword="null"/> if <paramref name="nodeId"/> does not resolve to an existing node.
    /// </summary>
    Task<QualityProfile?> GetQualityAsync(Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// §20.1's <c>search()</c> — calls Memory's <see cref="IKnowledgeClient.QueryAsync"/>
    /// internally, then applies §15.7's additive quality/relationship-aware ranking pass. Never
    /// bypasses or duplicates Memory's own retrieval/ranking (FR-KM3).
    /// </summary>
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// §20.1's <c>request_governance_action()</c> — emits <c>KnowledgeGovernanceActionRequested</c>
    /// (§19), then routes through <see cref="IProtectionClient.Validate"/> (FR-KM10) before any
    /// <see cref="EOS.KnowledgeGraph.KnowledgeMetadata.LifecycleState"/>/
    /// <see cref="EOS.KnowledgeGraph.KnowledgeMetadata.VersionHistory"/> change takes effect.
    /// Returns Protection's <see cref="ValidationResult"/> so the caller can observe Allow vs.
    /// Deny/Defer (§22.2's sequence: "action not applied, reason returned" on non-Allow). Throws
    /// <see cref="ArgumentException"/> if <paramref name="nodeId"/> does not resolve to an
    /// existing node.
    /// </summary>
    Task<ValidationResult> RequestGovernanceActionAsync(
        Guid nodeId, GovernanceActionType action, string justification, CancellationToken cancellationToken = default);

    /// <summary>
    /// §20.1's <c>find_duplicates()</c> — Duplicate Detection (§18.4), structural signals only
    /// (§18.3) with <see cref="ICompareProvider"/> consulted per candidate. Flags only, never
    /// merges — §18.5 requires a separate governance action. Throws
    /// <see cref="ArgumentException"/> if <paramref name="nodeId"/> does not resolve to an
    /// existing node.
    /// </summary>
    Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(
        Guid nodeId, CancellationToken cancellationToken = default);
}
