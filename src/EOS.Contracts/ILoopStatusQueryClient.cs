namespace EOS.Contracts;

/// <summary>
/// WP-030 (Dashboard, Constitution §0.11's read-only-projection rule) — a narrow, dashboard-only
/// read contract exposing exactly the same current-status projection
/// <see cref="ILoopControlClient.GetCurrentStatusAsync"/> already returns (<see cref="LoopStatus"/>,
/// reused unchanged — no new type), but deliberately without that interface's write-capable
/// members (<c>SetOperationalModeAsync</c>/<c>EmergencyStopAsync</c>) — Dashboard must never even
/// have compile-time access to those, per its own read-only-projection mandate. The composition
/// root may implement this and <see cref="ILoopControlClient"/> on the same underlying adapter;
/// only the contract surface differs, matching this codebase's own Composition Root Adapter
/// Pattern (ADR-015-001).
/// </summary>
public interface ILoopStatusQueryClient
{
    Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default);
}
