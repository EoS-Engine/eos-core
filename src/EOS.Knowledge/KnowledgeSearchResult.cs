using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §20.1's <c>search()</c> return element — a Memory
/// result paired with §15.7's additive <c>km_score</c>, after the stable re-sort.
/// </summary>
public sealed record KnowledgeSearchResult(KnowledgeNode Node, double Score);
