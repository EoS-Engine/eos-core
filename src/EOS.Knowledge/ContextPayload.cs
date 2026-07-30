using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §15.1/§15.2/§20.1. Per ADR-015-005, <see cref="Items"/>
/// is <see cref="KnowledgeNode"/>-only — no candidate-normalization type is introduced, since
/// the only in-scope source (<c>KnowledgeGraphStore</c>) already produces the exact type
/// <c>RetrievalRanking</c> already accepts.
/// </summary>
public sealed record ContextPayload(IReadOnlyList<KnowledgeNode> Items, bool Truncated);
