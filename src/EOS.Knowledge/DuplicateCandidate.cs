namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §20.1's <c>find_duplicates()</c> return element and
/// §19's <c>KnowledgeDuplicateFlagged</c> payload shape ("node_id_a, node_id_b, similarity_source
/// (Reasoning <c>compare()</c> ref or structural)"). <see cref="SimilaritySource"/> is
/// <c>"structural"</c> when only §18.3's structural signals (shared taxonomy/domain_tags/
/// relationship neighbors) matched, or <c>"Reasoning compare()"</c> when
/// <see cref="ICompareProvider"/> additionally affirmed semantic similarity.
/// </summary>
public sealed record DuplicateCandidate(Guid NodeId, string SimilaritySource);
