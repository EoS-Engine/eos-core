namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §12/§21.1's Knowledge Lifecycle state model:
/// "Creation → Validation → Approval → Promotion → Publication → Versioning (ongoing) →
/// Update | Deprecation | Archiving → Retirement → Recovery." Versioning is excluded as a
/// discrete state (the spec itself marks it "(ongoing)", a concurrent property of every state
/// rather than a position in the sequence — tracked instead by <see cref="VersionRecord"/>'s
/// own append-only chain, §12.6).
/// </summary>
public enum KnowledgeLifecycleState
{
    Creation,
    Validation,
    Approval,
    Promotion,
    Publication,
    Update,
    Deprecation,
    Archiving,
    Retirement,
    Recovery,
}
