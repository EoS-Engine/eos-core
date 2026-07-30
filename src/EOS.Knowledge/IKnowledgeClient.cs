using EOS.KnowledgeGraph;

namespace EOS.Knowledge;

public interface IKnowledgeClient
{
    Task UpdateAsync(
        Guid nodeId,
        KnowledgeNodeType nodeType,
        string content,
        string[] domainTags,
        string[] evidenceRefs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Memory-Management-Specification-v1.0 §10/§20.1. This is a Knowledge Graph query API —
    /// it returns only content represented as a <see cref="KnowledgeNode"/>. <see
    /// cref="MemoryType.Working"/>, <see cref="MemoryType.ShortTerm"/>, and <see
    /// cref="MemoryType.Session"/> are real storage strategies (Redis-backed, §10.1/§10.2/§10.7)
    /// but are never <see cref="KnowledgeNode"/> instances, so they are not queryable through
    /// this method — passing one of those three throws <see cref="NotSupportedException"/>.
    /// Their retrieval surface belongs to future runtime components that own ephemeral
    /// execution state (Reasoning/Orchestrator/Context Assembly), not to this Knowledge query
    /// API. Documented architectural limitation, WP-014.
    /// </summary>
    Task<IEnumerable<KnowledgeNode>> QueryAsync(
        MemoryType? type,
        string[]? domainTags,
        DateRange? range,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Memory-Management-Specification-v1.0 §20.1, consumed as-is by
    /// Learning-Engine-Specification-v1.1 §11.2/§14.3 ("<c>candidates =
    /// Knowledge.query_similar(record.knowledge_graph_ref)</c>"). That consumer treats the
    /// result as an unranked symbolic <b>candidate pool</b> — actual similarity judgment happens
    /// afterward, in a separate call to <c>ReasoningEngine.compare()</c>, never here (Memory
    /// Management §5 Non-Responsibilities: "Semantic similarity computation... Reasoning
    /// Engine"). This method resolves <paramref name="nodeId"/>, builds a symbolic candidate
    /// pool from other nodes sharing its <see cref="KnowledgeNodeType"/>, excludes the node
    /// itself (§20.1 postcondition), and orders the pool via the mechanical ranking formula
    /// (§19) using the resolved node's own domain tags as ranking scope. This is a purely
    /// symbolic operation — no embedding, no <c>EOS.VectorStore</c> involvement — consistent
    /// with §9's component diagram, which wires <c>EOS.VectorStore</c> only to
    /// <c>ContextAssembler</c> (<c>assemble_context()</c>, §15/WP-015), never to this method.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="nodeId"/> does not resolve to an existing node (precondition violation).
    /// </exception>
    Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(
        Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Memory-Management-Specification-v1.0 §15.1/§15.2/§20.1's budgeted, ranked context
    /// composition. Per ADR-015-005, this is symbolic-only this WP: <see
    /// cref="ContextRequest.IncludesEpisodic"/>/<see cref="ContextRequest.IncludesSemantic"/>
    /// are honored via <c>KnowledgeGraphStore</c>; <see cref="ContextRequest.IncludesWorking"/>/
    /// <see cref="ContextRequest.IncludesShortTerm"/> are structurally present but intentionally
    /// inert, since <c>KnowledgeClient</c> has no legal dependency path to
    /// <c>RedisMemoryStore</c> (<c>EOS.Infrastructure</c>). §15.2's truncation transparency is
    /// honored: <see cref="ContextPayload.Truncated"/> is always populated truthfully.
    /// </summary>
    Task<ContextPayload> AssembleContextAsync(
        ContextRequest request, CancellationToken cancellationToken = default);
}
