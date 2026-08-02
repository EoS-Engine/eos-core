namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §10.9's <c>knowledge_metadata</c> structure —
/// additive fields on the existing <see cref="KnowledgeNode"/> (§10.9: "additive fields, not a
/// redefinition"), never a new physical store (FR-KM1). WP-017 populated
/// <see cref="Taxonomy"/> and <see cref="Relationships"/>; WP-018 adds the remaining FR-KM2
/// fields. Per §13.1, Confidence and Freshness are not separate top-level fields — they are
/// attributes of the single <see cref="Quality"/> aggregate. "Version" (FR-KM2) is
/// <see cref="VersionHistory"/>, the append-only chain §12.6/FR-KM6 requires.
/// </summary>
public sealed record KnowledgeMetadata
{
    public TaxonomyClassification? Taxonomy { get; init; }

    public IReadOnlyList<RelationshipEdge> Relationships { get; init; } = [];

    public string? Owner { get; init; }

    public QualityProfile? Quality { get; init; }

    public string? Source { get; init; }

    public IReadOnlyList<VersionRecord> VersionHistory { get; init; } = [];

    public KnowledgeLifecycleState? LifecycleState { get; init; }

    public DateTimeOffset? LastValidation { get; init; }
}
