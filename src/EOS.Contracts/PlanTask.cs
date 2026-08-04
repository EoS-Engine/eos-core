namespace EOS.Contracts;

/// <summary>
/// One node of the ordered Task Graph Constitution §0.4.2 requires as part of a
/// <see cref="Plan"/> — "competency requirements" per that same definition.
/// <see cref="DependsOnTaskIds"/> realizes Planning-Execution-Engine-Specification-v1.0 §10.3's
/// DAG-of-Tasks decomposition. This is the Task Graph Builder's own planning-time record — the
/// Task itself becomes Constitution Part 6's existing Task entity only once the Scheduler
/// (§10.6, WP-024) dispatches it (Planning-Execution-Engine-Specification-v1.0 §13.1: "output
/// Tasks are exactly Constitution Part 6's existing Task entity").
/// </summary>
public sealed record PlanTask(
    Guid TaskId,
    string Description,
    string[] CompetencyRequirements,
    Guid[] DependsOnTaskIds);
