namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §14's nine Knowledge Relationship types.
/// </summary>
public enum RelationshipType
{
    DependsOn,
    DerivedFrom,
    References,
    RelatedTo,
    Replaces,
    Supersedes,
    ConflictsWith,
    Supports,
    Requires,
}
