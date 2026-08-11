namespace EOS.Contracts;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §18.1's <c>get_current_status()</c> read-only
/// response — "current iteration, current Operational Mode (§22), current loop_health_score"
/// (WP-028 Decision 2, locked). WP-028 always reports <see cref="CurrentMode"/> as
/// <see cref="OperationalMode.Assisted"/> (§22.2's stated default; mode switching is WP-029) and
/// <see cref="LoopHealthScore"/> as <c>null</c> — that value is computed exclusively by
/// Self-Evaluate (§13.1), which WP-028 does not implement; this field exists so WP-029 can
/// backfill it without a breaking type change, matching <c>RoiGate</c>'s own precedent of
/// representing "not yet computable" honestly rather than fabricating a value.
/// </summary>
public sealed record LoopStatus(Guid? CurrentIterationId, OperationalMode CurrentMode, double? LoopHealthScore);
