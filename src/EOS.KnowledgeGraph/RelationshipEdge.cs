namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §14's typed relationship edge, stored as a property
/// on the source node's <c>knowledge_metadata.relationships</c> array (§14.1) — never a
/// separate graph-edge store (FR-KM1). Holds only <see cref="TargetNodeId"/>, never a
/// <c>KnowledgeNode</c> reference, so an edge can never form a circular object graph or a
/// serialization cycle with its target.
/// <see cref="GovernanceApprovalRef"/> is required by the Ontology constraint (§14's table) for
/// <see cref="RelationshipType.Replaces"/>/<see cref="RelationshipType.Supersedes"/> edges only;
/// null for every other relationship type.
/// </summary>
public sealed record RelationshipEdge
{
    public required Guid TargetNodeId { get; init; }

    public required RelationshipType RelationshipType { get; init; }

    public string? GovernanceApprovalRef { get; init; }
}
