namespace EOS.Contracts;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §18.1 — WP-028 Decision 1 (locked): only
/// <see cref="GetCurrentStatusAsync"/> is declared here, matching the roadmap's own Architecture
/// Traceability Matrix split (<c>get_current_status</c> -> WP-028; <c>set_operational_mode</c>/
/// <c>emergency_stop</c> -> WP-029). WP-029 adds those two methods to this same interface later
/// as a non-breaking extension — mirroring <c>IPipelineRecordStore</c>'s own precedent (WP-027)
/// of gaining members across Work Packages. The method is declared <c>async</c>/<c>Task</c>-
/// returning rather than the spec's synchronous pseudocode notation, matching every other public
/// client interface in <c>EOS.Contracts</c> (<c>IPlanningClient</c>, <c>IReasoningEngineClient</c>,
/// etc.) — the spec's pseudocode is language-agnostic, not literal C#.
/// </summary>
public interface ILoopControlClient
{
    Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default);
}
