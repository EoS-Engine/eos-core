namespace EOS.Orchestrator;

/// <summary>
/// Constitution Part 3's existing <c>TaskBlocked</c> event (producer: "Any role, EOS.Gates";
/// payload: <c>task_id, blocking_gate/reason</c>), reused verbatim — Post-Roadmap WP-A's
/// Execution Coordinator is its first real producer, publishing it only after the Constitution
/// Part 6 §6.2 <c>Running → Blocked</c> write (with its <c>BlockedReason</c> root-cause note)
/// has committed, per the Composition Root Adapter Pattern (ADR-015-001).
/// </summary>
public interface ITaskBlockedEventPublisher
{
    void PublishTaskBlocked(Guid taskId, string reason);
}
