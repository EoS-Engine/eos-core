namespace EOS.Orchestrator;

/// <summary>
/// Constitution Part 3's existing <c>TaskCompleted</c> event (payload: <c>task_id,
/// evidence_refs[]</c>), reused verbatim (Planning-Execution-Engine-Specification-v1.0 §20)
/// — Post-Roadmap WP-A's Execution Coordinator is its first real producer, per the Composition
/// Root Adapter Pattern (ADR-015-001). Published only after the <c>Running → Review</c> write
/// has committed (Constitution §0.15.1: a <c>TaskCompleted</c> must reference passing evidence
/// in the Artifact Registry, never just a status flag).
/// </summary>
public interface ITaskCompletedEventPublisher
{
    void PublishTaskCompleted(Guid taskId, string[] evidenceRefs);
}
