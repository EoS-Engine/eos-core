namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §13's <c>QualityProfile</c> — "a single structured
/// value (not ten independent free-floating scores)" (§13.1), attached to
/// <see cref="KnowledgeMetadata.Quality"/>. Per FR-KM9 and §13's own per-attribute "Knowledge
/// Management's Role" column, every attribute here except <see cref="Completeness"/> and
/// <see cref="Freshness"/> is sourced from its owning subsystem and only recorded/tracked here,
/// never independently recomputed. <see cref="Completeness"/> and <see cref="Freshness"/> are
/// the two attributes Knowledge Management itself computes (§13: "the one quality attribute
/// Knowledge Management does own the computation of" — Freshness; Completeness is a structural,
/// not semantic, computation). Property-based record — same construction-site-stability
/// rationale already applied to <see cref="KnowledgeMetadata"/>.
/// </summary>
public sealed record QualityProfile
{
    public double? Confidence { get; init; }

    public double? Accuracy { get; init; }

    public double? Completeness { get; init; }

    public double? Freshness { get; init; }

    public double? Reliability { get; init; }

    public VerificationStatus? VerificationStatus { get; init; }

    public double? SourceQuality { get; init; }

    public double? EngineeringImpact { get; init; }

    public double? BusinessImpact { get; init; }

    // AG-0002, Finding 1: no Work Package (including this one) is assigned implementation of
    // the "Reuse Engine activity log" §13 names as this value's computation source. Structurally
    // present per §13.1's single-aggregate requirement; never populated by WP-018's own code.
    public double? Reusability { get; init; }
}
