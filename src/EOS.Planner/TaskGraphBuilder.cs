using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;

namespace EOS.Planner;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.3: decomposes a validated Goal (§11.5) into
/// a DAG of Tasks (§13), assigning each Task the competency requirements Constitution §0.4.2
/// already requires. Consults Memory's Semantic Memory (§12.6) for reusable planning patterns
/// and may issue a single bounded Reasoning Engine delegation (§10.11, ADR-PE003) for a specific
/// ambiguous decomposition judgment call — this class alone ever assembles the final DAG
/// (FR-PE4).
/// </summary>
public sealed class TaskGraphBuilder(IKnowledgeClient knowledgeClient, IReasoningEngineClient reasoningEngineClient)
{
    public async Task<PlanTask[]> DecomposeAsync(Goal goal, CancellationToken cancellationToken)
    {
        // §12.6: "queries Memory's IKnowledgeClient.query()... for Semantic Memory content
        // tagged as a planning-relevant Pattern/Best Practice/Principle" — KnowledgeNodeType's
        // only such tag is Pattern (EOS.KnowledgeGraph.KnowledgeNodeType); MemoryType.Semantic
        // resolves to [Fact, Pattern] (KnowledgeClient.ResolveNodeTypes), so Pattern-typed nodes
        // are filtered client-side. Read-only; never queries EOS.KnowledgeGraph/EOS.VectorStore
        // directly (§12.6's explicit prohibition).
        var semanticNodes = await knowledgeClient.QueryAsync(MemoryType.Semantic, goal.DomainTags, range: null, cancellationToken);
        var patterns = semanticNodes.Where(node => node.NodeType == KnowledgeNodeType.Pattern).ToList();

        KnowledgeNode? selectedPattern = patterns.Count switch
        {
            0 => null,
            1 => patterns[0],
            _ => await SelectAmbiguousPatternAsync(goal, patterns, cancellationToken),
        };

        return BuildTasks(goal, selectedPattern);
    }

    // §10.11/ADR-PE003: "a single, bounded judgment call feeding one specific decomposition or
    // sequencing decision... never for the plan as a whole." Reached only when more than one
    // candidate reusable pattern exists for the same Goal — the Reasoning Engine's response
    // (Decision.SelectedHypothesis) only ever picks among the already-retrieved candidate
    // patterns; it never generates a Task itself (FR-PE4). If the response does not identify a
    // known candidate, the first retrieved pattern is used — the delegation narrows a choice, it
    // never becomes the sole source of truth for that choice.
    private async Task<KnowledgeNode> SelectAmbiguousPatternAsync(
        Goal goal, IReadOnlyList<KnowledgeNode> patterns, CancellationToken cancellationToken)
    {
        var request = new ReasoningRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: goal.GoalId,
            Goal: $"Which of these {patterns.Count} reusable planning patterns best fits the goal '{goal.Statement}'? Candidates: {string.Join(", ", patterns.Select(p => p.NodeId))}.",
            RequestingRole: "EOS.Planner",
            ReasoningType: ReasoningType.GoalOrientedReasoning);

        var decisions = await reasoningEngineClient.ReasonAsync(request, cancellationToken);
        var decision = decisions[0];

        return patterns.FirstOrDefault(p => decision.SelectedHypothesis.Contains(p.NodeId.ToString(), StringComparison.OrdinalIgnoreCase))
            ?? patterns[0];
    }

    // No frozen document specifies a decomposition algorithm (§10.3 says only "decomposes...
    // into a DAG of Tasks," never how) — this is the minimal, deterministic, disclosed rule:
    // a matched pattern's content is split into sequential steps by line, each depending on the
    // previous (a genuine, schema-valid DAG); with no pattern, the Goal's own statement becomes
    // the single Task. Both are FR-PE3-deterministic given the same Goal/pattern.
    private static PlanTask[] BuildTasks(Goal goal, KnowledgeNode? pattern)
    {
        var steps = pattern is not null
            ? pattern.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [goal.Statement];

        if (steps.Length == 0)
        {
            steps = [goal.Statement];
        }

        var tasks = new PlanTask[steps.Length];
        Guid? previousTaskId = null;
        for (var i = 0; i < steps.Length; i++)
        {
            var taskId = Guid.NewGuid();
            tasks[i] = new PlanTask(
                TaskId: taskId,
                Description: steps[i],
                CompetencyRequirements: goal.DomainTags,
                DependsOnTaskIds: previousTaskId is { } previous ? [previous] : []);
            previousTaskId = taskId;
        }

        return tasks;
    }
}
