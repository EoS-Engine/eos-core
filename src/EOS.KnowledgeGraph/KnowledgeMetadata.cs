namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §10.9's <c>knowledge_metadata</c> structure —
/// additive fields on the existing <see cref="KnowledgeNode"/> (§10.9: "additive fields, not a
/// redefinition"), never a new physical store (FR-KM1). WP-017 populates only
/// <see cref="Taxonomy"/> and <see cref="Relationships"/>; the remaining FR-KM2 fields (owner,
/// confidence, quality, source, version, lifecycle_state, last_validation, freshness) are
/// WP-018's scope and are added as further init-only properties on this record without
/// modifying its construction model.
/// </summary>
public sealed record KnowledgeMetadata
{
    public TaxonomyClassification? Taxonomy { get; init; }

    public IReadOnlyList<RelationshipEdge> Relationships { get; init; } = [];
}
