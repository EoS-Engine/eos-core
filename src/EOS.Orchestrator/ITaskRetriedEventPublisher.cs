namespace EOS.Orchestrator;

/// <summary>
/// Constitution Part 3's <c>TaskRetried</c> event, reused verbatim
/// (Planning-Execution-Engine-Specification-v1.0 §20: "Existing events... reused verbatim, never
/// redefined"; producer: Retry Manager, §10.9) — per the Composition Root Adapter Pattern
/// (ADR-015-001). Payload frozen by WP-025 Architecture Board Ruling Q4 to exactly
/// (task_id, attempt_number) — no "reason" field.
/// </summary>
public interface ITaskRetriedEventPublisher
{
    void PublishTaskRetried(Guid taskId, int attemptNumber);
}
