namespace EOS.Contracts;

/// <summary>
/// The outcome of one successful <see cref="ITaskExecutionClient.ExecuteAsync"/> call:
/// the evidence references (<c>artifact:&lt;sha256&gt;</c>, Constitution Part 6 §6.3) the
/// executing role registered for the Task. Failure is never represented here — an executing
/// role that cannot produce evidence throws, and the Execution Coordinator records the
/// Constitution Part 6 §6.2 <c>Running → Blocked</c> transition.
/// </summary>
public sealed record TaskExecutionResult(string[] EvidenceRefs);
