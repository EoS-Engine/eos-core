namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §19.1's Loop Iteration Lifecycle, persisted —
/// WP-028 Decision 4 (locked). <see cref="State"/> is deliberately <c>string</c>, not an enum:
/// WP-028 writes only <c>Triggered, Observing, Deciding, Executing, Learning, Completed, Failed</c>
/// — <c>Evaluating</c>/<c>Improving</c> (steps 16-17) are reserved, unused future string values
/// WP-029 can write without any schema or type change, never redefined or removed by this WP.
/// <see cref="StepsTraversed"/>/<see cref="Outcome"/> match <c>LoopIterationCompleted</c>'s named
/// payload fields exactly (§17).
/// </summary>
public sealed record LoopIteration(
    Guid IterationId,
    string TriggerSource,
    int EntryStep,
    string State,
    int[] StepsTraversed,
    string? Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
