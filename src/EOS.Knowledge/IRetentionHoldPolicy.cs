namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §17.1's Compression eligibility sub-criterion: an entry
/// "flagged with a legal/compliance retention hold (§26)" is not eligible. §26 states Memory
/// "enforces the hold but does not itself decide when a hold applies (that determination
/// belongs to whichever role/policy sets the flag, out of this specification's scope per §5)."
/// No policy source exists yet anywhere in this codebase to set such a flag. Per the
/// Composition Root Adapter Pattern (ADR-015-001 precedent): <c>EOS.Knowledge</c> defines this
/// small interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter — an
/// honestly-documented stub (always "no active hold") until a real hold-setting policy source
/// exists.
/// </summary>
public interface IRetentionHoldPolicy
{
    Task<bool> HasActiveHoldAsync(Guid episodicEntryId, CancellationToken cancellationToken = default);
}
