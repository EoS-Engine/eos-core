using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §19.2's Operational Mode State — "persists
/// across iterations" — owned by <c>EOS.Orchestrator</c>, mirroring <see cref="ILoopIterationStore"/>'s
/// exact ownership posture (WP-029 Implementation Plan §5/§14). A single current-value row, not a
/// history table — §19.2 describes one active mode at a time, not a log of past modes (that is
/// what <c>OperationalModeChanged</c>, §17, already provides for anyone needing history).
/// </summary>
public interface IOperationalModeStore
{
    Task EnsureTableExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Autonomous-Engineering-Loop-Specification-v1.0 §22.2: Assisted is "the Loop's default mode
    /// in the absence of an explicit selection" — returned when no mode has ever been persisted,
    /// matching WP-028's own prior hard-coded default in <c>GetCurrentStatusAsync</c>.
    /// </summary>
    Task<OperationalMode> GetCurrentModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// CodeRabbit pre-merge P1 finding #2 fix: persists <paramref name="mode"/> and returns the
    /// mode that was authoritative immediately beforehand, both determined by the same atomic
    /// database operation — never a separately-read value, which under genuine concurrent callers
    /// could be stale by the time it is used to construct <c>OperationalModeChanged</c>'s
    /// <c>from_mode</c> (§17). Returns <see cref="OperationalMode.Assisted"/> (§22.2's default)
    /// when this is the first mode ever persisted.
    /// </summary>
    Task<OperationalMode> SetCurrentModeAsync(OperationalMode mode, CancellationToken cancellationToken = default);
}
