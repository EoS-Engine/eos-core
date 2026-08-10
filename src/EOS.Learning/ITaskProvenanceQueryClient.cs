namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.5's Feedback Loop Guard: "<c>downstream_tasks =
/// Planner.tasks_generated_from(record)  # via Contracts, read-only query</c>... <c>if
/// task.outcome_feeds(record.knowledge_graph_ref): flag_as_self_referential(task)</c>". Per the
/// Composition Root Adapter Pattern (ADR-015-001 precedent, matching <c>IPipelineStageStore</c>'s
/// WP-016 shape): <c>EOS.Learning</c> declares this small interface; <c>EOS.Runner</c>'s
/// <c>Program.cs</c> supplies the concrete adapter (which legally references <c>EOS.Planner</c>,
/// unlike <c>EOS.Learning</c> itself). The two spec steps (find downstream tasks, then check
/// whether each one's outcome feeds back) are combined into a single query here, since no
/// existing production code anywhere in this repository tracks either "which tasks were
/// generated from a given pattern" or "does a task's own outcome feed a given KnowledgeGraphRef"
/// — <c>DispatchedTask</c> (EOS.Orchestrator) carries no field linking a task back to the
/// pattern that informed its creation.
/// </summary>
public interface ITaskProvenanceQueryClient
{
    /// <summary>Returns the IDs of downstream tasks generated from <paramref name="knowledgeGraphRef"/> whose own outcome feeds back into that same reference.</summary>
    Task<IReadOnlyList<Guid>> GetSelfReferentialTaskIdsAsync(Guid knowledgeGraphRef, CancellationToken cancellationToken = default);
}
