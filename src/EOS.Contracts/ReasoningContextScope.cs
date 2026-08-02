namespace EOS.Contracts;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §13.2's <c>context_scope: { domain_tags[], project_scope,
/// budget }</c> — the value <see cref="ReasoningRequest.ContextScope"/> passes to Memory's
/// <c>assemble_context()</c> (§12.1). Defined here, in <c>EOS.Contracts</c>, rather than reusing
/// <c>EOS.Knowledge.ContextRequest</c>'s near-matching fields, since <c>EOS.Contracts</c> cannot
/// reference <c>EOS.Knowledge</c> (Constitution Part 1 §1.2).
/// </summary>
public sealed record ReasoningContextScope(string[]? DomainTags, string[]? ProjectScope, int? Budget);
