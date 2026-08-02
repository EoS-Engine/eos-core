namespace EOS.Reasoning;

/// <summary>
/// Reasoning-Engine-Specification-v1.0 §12.1's <c>ContextPayload</c>, translated into an
/// <c>EOS.Reasoning</c>-local shape by the <see cref="IContextAcquisitionProvider"/> adapter,
/// since <c>EOS.Reasoning</c> cannot reference <c>EOS.Knowledge</c>'s own <c>ContextPayload</c>/
/// <c>KnowledgeNode</c> types (Constitution Part 1 §1.2). <see cref="Items"/> holds each
/// retrieved item's content text; <see cref="Truncated"/> mirrors Memory-Management-
/// Specification-v1.0 §15.2's truncation-transparency field verbatim.
/// </summary>
public sealed record AcquiredContext(IReadOnlyList<string> Items, bool Truncated);
