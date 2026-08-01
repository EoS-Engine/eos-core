using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

public sealed record OntologyValidationResult(bool IsValid, string? Reason);

/// <summary>
/// Knowledge-Management-Specification-v1.0 §10.7's Ontology Support, validating only the
/// specific, named constraints §14's Relationship table actually states — never a general-
/// purpose N×M ontology-engineering framework (§10.7 explicitly disclaims that):
/// <list type="bullet">
/// <item><see cref="RelationshipType.DependsOn"/> — "a Fact may not Depend On a Lesson
/// (pre-promotion content cannot be a dependency of settled knowledge)". The disallowed target
/// type set is externally configurable (§10.7: "externally configurable (`Knowledge.json`...)
/// rather than hardcoded"), via <paramref name="dependsOnDisallowedTargetTypes"/>.</item>
/// <item><see cref="RelationshipType.Replaces"/>/<see cref="RelationshipType.Supersedes"/> —
/// "Requires a Governance approval reference (§16.3) — never auto-assigned." Which relationship
/// types require this is likewise externally configurable, via
/// <paramref name="governanceApprovalRequiredRelationshipTypes"/>.</item>
/// <item><see cref="RelationshipType.Requires"/> — "a Requires edge with a missing... target
/// fails structural validation." A referential-integrity invariant, not an Ontology
/// participation rule, so it is not externally configurable.</item>
/// </list>
/// Every other relationship type (DerivedFrom, References, RelatedTo, ConflictsWith, Supports)
/// has no stated Ontology constraint in §14 and is therefore always valid here.
/// </summary>
public sealed class OntologyValidator(
    KnowledgeGraphStore store,
    IReadOnlyList<KnowledgeNodeType> dependsOnDisallowedTargetTypes,
    IReadOnlyList<RelationshipType> governanceApprovalRequiredRelationshipTypes)
{
    public async Task<OntologyValidationResult> ValidateAsync(
        RelationshipEdge edge, CancellationToken cancellationToken = default)
    {
        if (edge.RelationshipType == RelationshipType.DependsOn)
        {
            var dependsOnTarget = await store.GetByIdAsync(edge.TargetNodeId, cancellationToken);
            if (dependsOnTarget is not null && dependsOnDisallowedTargetTypes.Contains(dependsOnTarget.NodeType))
            {
                return new OntologyValidationResult(
                    IsValid: false,
                    Reason: $"A DependsOn edge may not target a {dependsOnTarget.NodeType} node — " +
                        "Knowledge-Management-Specification-v1.0 §14.");
            }
        }

        if (governanceApprovalRequiredRelationshipTypes.Contains(edge.RelationshipType)
            && string.IsNullOrWhiteSpace(edge.GovernanceApprovalRef))
        {
            return new OntologyValidationResult(
                IsValid: false,
                Reason: $"A {edge.RelationshipType} edge requires a governance approval reference — " +
                    "Knowledge-Management-Specification-v1.0 §14.");
        }

        if (edge.RelationshipType == RelationshipType.Requires)
        {
            var requiresTarget = await store.GetByIdAsync(edge.TargetNodeId, cancellationToken);
            if (requiresTarget is null)
            {
                return new OntologyValidationResult(
                    IsValid: false,
                    Reason: "A Requires edge's target must exist — Knowledge-Management-Specification-v1.0 §14.");
            }
        }

        return new OntologyValidationResult(IsValid: true, Reason: null);
    }
}
