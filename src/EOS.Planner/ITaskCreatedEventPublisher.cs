namespace EOS.Planner;

/// <summary>
/// Constitution Part 3's existing <c>TaskCreated</c> event, reused verbatim
/// (Planning-Execution-Engine-Specification-v1.0 §20: "Existing events... reused verbatim,
/// never redefined"; producer: Task Graph Builder, §13.1) — per the Composition Root Adapter
/// Pattern (ADR-015-001). Published by <see cref="PlanningEngine"/>, once priority (§10.5) is
/// known for each <c>PlanTask</c> — the event's own payload requires it.
/// </summary>
public interface ITaskCreatedEventPublisher
{
    void PublishTaskCreated(Guid taskId, string[] competenciesRequired, int priority);
}
