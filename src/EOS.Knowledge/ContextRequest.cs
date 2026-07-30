namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §15.1/§20.1. <see cref="IncludesWorking"/> and
/// <see cref="IncludesShortTerm"/> are structurally present per §15.1's evidenced fields but
/// are intentionally inert this WP (ADR-015-005) — <c>KnowledgeClient</c> has no legal
/// dependency path to <c>RedisMemoryStore</c> (<c>EOS.Infrastructure</c>), so ephemeral
/// Working/Short-term Memory is not assembled into <see cref="ContextPayload"/> this WP.
/// </summary>
public sealed record ContextRequest(
    int TokenOrSizeBudget,
    bool IncludesWorking,
    bool IncludesShortTerm,
    bool IncludesEpisodic,
    bool IncludesSemantic,
    string[]? ProjectScope,
    DateRange? Filters,
    Guid? TaskId);
