using EOS.Contracts;
using EOS.KnowledgeGraph;

namespace EOS.Planner.Tests;

/// <summary>
/// ADR-PE003/FR-PE4 enforcement — roadmap Test Verification: "an integration test confirming
/// the bounded Reasoning delegation never returns a full task graph." Exercised via
/// <see cref="TaskGraphBuilder"/>'s ambiguous-pattern path (2+ candidate reusable patterns),
/// the one place this WP issues a Reasoning Engine delegation.
/// </summary>
public class ReasoningDelegationBoundaryTests
{
    private static Goal CreateGoal() =>
        new(Guid.NewGuid(), "Add a logging statement to module X", null, ["logging"], "Product Owner", GoalLifecycleState.Decomposing, null);

    private static KnowledgeNode Pattern(string content) =>
        new(Guid.NewGuid(), KnowledgeNodeType.Pattern, content, ["logging"], [], DateTimeOffset.UtcNow);

    [Fact]
    public async Task DecomposeAsync_IssuesAtMostOneReasoningDelegation_EvenWhenMultiplePatternsAreCandidates()
    {
        var patterns = new[] { Pattern("Approach A"), Pattern("Approach B"), Pattern("Approach C") };
        var reasoningClient = new CountingReasoningEngineClient(patterns[1].NodeId.ToString());
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient(patterns), reasoningClient);

        await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        Assert.Equal(1, reasoningClient.CallCount);
    }

    [Fact]
    public async Task DecomposeAsync_TheReasoningResponseOnlySelectsAmongPreBuiltCandidates_NeverGeneratesATaskItself()
    {
        var patterns = new[] { Pattern("Approach A"), Pattern("Approach B") };
        // The Reasoning Engine's response selects the second pattern by id — the Task Graph
        // Builder must use that pattern's own already-existing content, never text invented by
        // the Decision itself (FR-PE4: "MUST NOT generate a Task Graph from a Reasoning Engine
        // response alone").
        var reasoningClient = new CountingReasoningEngineClient(patterns[1].NodeId.ToString());
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient(patterns), reasoningClient);

        var tasks = await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        var task = Assert.Single(tasks);
        Assert.Equal("Approach B", task.Description);
    }

    [Fact]
    public async Task DecomposeAsync_FallsBackToTheFirstCandidate_WhenTheReasoningResponseMatchesNoKnownPattern()
    {
        // Even if the Reasoning Engine's response is unusable (matches no retrieved candidate),
        // the Task Graph Builder still deterministically resolves to one of its own pre-built
        // candidates — the response can only ever narrow a choice among what the Task Graph
        // Builder itself already retrieved, never supply new content of its own.
        var patterns = new[] { Pattern("Approach A"), Pattern("Approach B") };
        var reasoningClient = new CountingReasoningEngineClient(selectedHypothesis: "this matches nothing real");
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient(patterns), reasoningClient);

        var tasks = await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        var task = Assert.Single(tasks);
        Assert.Equal("Approach A", task.Description);
        Assert.Equal(1, reasoningClient.CallCount);
    }

    [Fact]
    public async Task DecomposeAsync_DoesNotInvokeReasoning_WhenDecompositionIsUnambiguous()
    {
        // FR-PE3: deterministic given the same inputs, reserving non-determinism only for the
        // bounded delegation case — 0 or 1 candidate is never ambiguous.
        var reasoningClient = new CountingReasoningEngineClient("unused");
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient([]), reasoningClient);

        await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        Assert.Equal(0, reasoningClient.CallCount);
    }
}
