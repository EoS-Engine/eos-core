using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record KnowledgeOptions
{
    [Required, MinLength(1)]
    public required string VectorStoreCollection { get; init; }

    // Knowledge-Management-Specification-v1.0 §10.7's Ontology Support: "externally
    // configurable (Knowledge.json...) rather than hardcoded." Values are RelationshipType/
    // KnowledgeNodeType names (EOS.KnowledgeGraph), read as plain strings here since
    // EOS.SharedKernel has no legal dependency on EOS.KnowledgeGraph.
    [Required]
    public required IReadOnlyList<string> DependsOnDisallowedTargetTypes { get; init; }

    [Required]
    public required IReadOnlyList<string> GovernanceApprovalRequiredRelationshipTypes { get; init; }

    // §17.1's decay_function, externally configurable (Knowledge.json). Half-life form: the
    // number of days for FreshnessScore's decay factor to halve.
    [Range(0.0001, double.MaxValue)]
    public required double FreshnessDecayHalfLifeDays { get; init; }

    // §17.1's type_weight(taxonomy), externally configurable (Knowledge.json). Keys are
    // TaxonomyClassification (EOS.KnowledgeGraph) names, read as plain strings here since
    // EOS.SharedKernel has no legal dependency on EOS.KnowledgeGraph. A taxonomy absent from
    // this map uses a neutral 1.0 weight.
    [Required]
    public required IReadOnlyDictionary<string, double> FreshnessTypeWeights { get; init; }

    // §17.3's Expiration Rule threshold — a FreshnessScore below this value flags the node for
    // Revalidation via KnowledgeFreshnessExpired (§19), never a Memory storage action (FR-KM7).
    [Range(0.0, 1.0)]
    public required double FreshnessExpirationThreshold { get; init; }

    // §15.7's q1..q4 ranking weights, independent of Memory's own w1..w4 (RankingWeights).
    [Required]
    public required double RankingConfidenceWeight { get; init; }

    [Required]
    public required double RankingReliabilityWeight { get; init; }

    [Required]
    public required double RankingRelationshipRelevanceWeight { get; init; }

    [Required]
    public required double RankingDeprecationPenaltyWeight { get; init; }
}
