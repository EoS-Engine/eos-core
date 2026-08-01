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
}
