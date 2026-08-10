namespace EOS.Orchestrator;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §20's fully specified <c>RollbackExecuted</c>
/// event (producer: Rollback Manager, §10.10) — per the Composition Root Adapter Pattern
/// (ADR-015-001). Must only be published after the corresponding Rollback Path transition has
/// already committed (WP-025 Architecture Board Ruling Q3: no false-success publication).
/// </summary>
public interface IRollbackExecutedEventPublisher
{
    void PublishRollbackExecuted(Guid taskId, string rollbackPathUsed);
}
