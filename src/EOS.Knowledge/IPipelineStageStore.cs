namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §17.1's Compression eligibility check requires knowing
/// whether an Episodic entry's corresponding <c>PipelineRecord</c>
/// (Learning-Engine-Specification-v1.1 §9) has reached <c>Pattern</c> stage or beyond.
/// <c>PipelineRecord</c> is exclusively owned by <c>EOS.Learning</c>
/// (Learning-Engine-Specification-v1.1 §7, Ownership) and does not exist until WP-026. Per the
/// Composition Root Adapter Pattern (ADR-015-001 precedent): <c>EOS.Knowledge</c> defines this
/// small interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter. Until a
/// real, WP-026-backed adapter exists, no entry can be proven to have reached <c>Pattern</c>
/// stage, so a stub that always reports "not yet promoted" is the architecturally correct
/// answer, not a placeholder — this WP stubs the check until WP-026 exists, then is revisited,
/// exactly mirroring how this same WP stubs <c>EOS.Reasoning</c>'s <c>summarize()</c> until
/// WP-020 (see <see cref="ISummarizer"/>).
/// </summary>
public interface IPipelineStageStore
{
    Task<bool> HasReachedPatternStageOrBeyondAsync(Guid episodicEntryId, CancellationToken cancellationToken = default);
}
