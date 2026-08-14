namespace EOS.Contracts;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §18.1 — WP-028 Decision 1 (locked): only
/// <see cref="GetCurrentStatusAsync"/> was declared here, matching the roadmap's own Architecture
/// Traceability Matrix split (<c>get_current_status</c> -> WP-028; <c>set_operational_mode</c>/
/// <c>emergency_stop</c> -> WP-029). WP-029 now adds those two methods to this same interface as
/// the non-breaking extension WP-028 explicitly reserved — mirroring <c>IPipelineRecordStore</c>'s
/// own precedent (WP-027) of gaining members across Work Packages. Methods are declared
/// <c>async</c>/<c>Task</c>-returning rather than the spec's synchronous pseudocode notation,
/// matching every other public client interface in <c>EOS.Contracts</c> — the spec's pseudocode is
/// language-agnostic, not literal C#.
///
/// Both new methods return <see cref="ValidationResult"/> directly (§22.9: "the result of the
/// Protection Decision Matrix... is the sole authority") rather than a purpose-built wrapper type
/// — the Verdict/Tier/Reason it already carries is exactly what a caller needs to know whether the
/// request was allowed, denied, or deferred; WP-029's own governance decisions forbid inventing a
/// new wrapper type where an existing one already fits (Decision 2's "no new wrapper type" spirit,
/// applied here by analogy).
/// </summary>
public interface ILoopControlClient
{
    Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Autonomous-Engineering-Loop-Specification-v1.0 §22.9 — every Operational Mode change is
    /// itself a Decision-Matrix-governed action, routed through the exact same
    /// <see cref="IProtectionClient.Validate"/> gate as any other action (WP-029 Decision 4: the
    /// Loop selects, never self-approves). On <see cref="ProtectionVerdict.Allow"/>, the mode is
    /// persisted and <c>OperationalModeChanged</c> (§17) is published; on any other verdict, the
    /// mode is left unchanged and the verdict/reason are returned to the caller.
    /// </summary>
    Task<ValidationResult> SetOperationalModeAsync(
        OperationalMode mode, string requestedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Autonomous-Engineering-Loop-Specification-v1.0 §14.4 — identical to Protection Layer's own
    /// Emergency Shutdown (Protection-Layer-Specification-v1.0 §26.1); this delegates entirely to
    /// the existing <see cref="IProtectionClient.Validate"/>/<c>EmergencyShutdownState</c>
    /// mechanism rather than introducing a second, competing emergency-stop implementation
    /// (WP-029 Decision 4). <paramref name="reason"/> mirrors the frozen pseudocode's own
    /// <c>emergency_stop(requested_by, reason)</c> signature for interface fidelity, but has no
    /// carrying field on the existing, unmodified <see cref="ActionRequest"/> — it is accepted here
    /// for caller-side traceability only, and is not threaded into the Protection call.
    /// </summary>
    Task<ValidationResult> EmergencyStopAsync(
        string requestedBy, string reason, CancellationToken cancellationToken = default);
}
