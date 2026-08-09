namespace EOS.Orchestrator;

/// <summary>
/// Constitution Part 3's existing <c>TaskStarted</c> event, reused verbatim
/// (Planning-Execution-Engine-Specification-v1.0 §20: "Existing events... reused verbatim, never
/// redefined"; producer: Execution Coordinator, §10.7) — per the Composition Root Adapter
/// Pattern (ADR-015-001). WP-021/022's <c>ResourceMonitor</c> already subscribes to this exact
/// event's payload type (<c>Program.cs</c>'s own pre-existing <c>TaskStartedPayload</c>) with no
/// producer until now.
/// </summary>
public interface ITaskStartedEventPublisher
{
    void PublishTaskStarted(Guid taskId);
}
