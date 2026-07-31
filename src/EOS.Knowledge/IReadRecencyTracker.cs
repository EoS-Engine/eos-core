namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §17.1's Compression eligibility sub-criterion: an entry
/// is only eligible if it "has not been read via <c>IKnowledgeClient</c> in the last N Sprint
/// cycles (configurable, <c>Thresholds.json</c>)." No read-access-tracking mechanism exists
/// anywhere in this codebase — the same, already-disclosed gap <c>RetrievalRanking.cs</c>
/// records for its own <c>AccessFrequency</c> term (WP-014). Per the Composition Root Adapter
/// Pattern (ADR-015-001 precedent): <c>EOS.Knowledge</c> defines this small interface;
/// <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter — an honestly-documented
/// stub (always "not read recently," the permissive default that never blocks eligibility on
/// data nothing in this codebase can currently supply) until a real tracking mechanism exists.
/// </summary>
public interface IReadRecencyTracker
{
    Task<bool> WasReadRecentlyAsync(Guid episodicEntryId, CancellationToken cancellationToken = default);
}
