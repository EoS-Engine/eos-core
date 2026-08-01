namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §11's Knowledge Type taxonomy — a finer-grained
/// classification tag layered on top of Constitution §0.5.1's base node types and Learning
/// Engine's promoted pipeline stages, stored in <c>knowledge_metadata.taxonomy</c> (§10.9).
/// Never a competing content taxonomy (Memory-Management-Specification-v1.0 §6).
/// </summary>
public enum TaxonomyClassification
{
    Facts,
    Rules,
    Patterns,
    BestPractices,
    LessonsLearned,
    EngineeringStandards,
    ArchitectureDecisions,
    ProjectKnowledge,
    DomainKnowledge,
    OperationalKnowledge,
    UserKnowledge,
    AIKnowledge,
    ReferenceKnowledge,
}
