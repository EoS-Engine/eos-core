namespace EOS.Contracts;

/// <summary>
/// Constitution Part 6 §6.2's "Role executes" half of the <c>Ready → Running</c> row, and the
/// "Actor role" of the <c>Running → Review</c> row (evidence: "Implementation evidence (diff,
/// artifact)") — the contract through which <c>EOS.Orchestrator</c>'s Execution Coordinator
/// hands a dispatched Task to an Autonomous Role (Constitution §0.2). Declared in
/// <c>EOS.Contracts</c> because the Orchestrator may not reference role projects directly and
/// roles may not reference the Orchestrator (Constitution Part 2 §2.1 rule 7); the composition
/// root (<c>EOS.Runner</c>) wires the implementation.
/// </summary>
public interface ITaskExecutionClient
{
    /// <summary>
    /// Executes <paramref name="task"/> and returns the registered evidence references. Throws
    /// on any failure to produce evidence (the caller records <c>Running → Blocked</c>); never
    /// transitions the Task's own lifecycle state — that remains the Execution Coordinator's
    /// chokepoint (Planning-Execution-Engine-Specification-v1.0 §10.7, §22.4).
    /// </summary>
    Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default);
}
