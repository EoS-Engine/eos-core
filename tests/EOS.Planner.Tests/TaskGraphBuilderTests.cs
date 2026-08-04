using EOS.Contracts;
using EOS.KnowledgeGraph;

namespace EOS.Planner.Tests;

public class TaskGraphBuilderTests
{
    private static Goal CreateGoal(string statement = "Add a logging statement to module X") =>
        new(Guid.NewGuid(), statement, null, ["logging"], "Product Owner", GoalLifecycleState.Decomposing, null);

    [Fact]
    public async Task DecomposeAsync_ProducesASingleTask_WhenNoReusablePatternExists()
    {
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient([]), new CountingReasoningEngineClient("unused"));

        var tasks = await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        var task = Assert.Single(tasks);
        Assert.Equal("Add a logging statement to module X", task.Description);
        Assert.Equal(["logging"], task.CompetencyRequirements);
        Assert.Empty(task.DependsOnTaskIds);
    }

    [Fact]
    public async Task DecomposeAsync_CitesTheReusablePattern_WhenExactlyOneExists()
    {
        var pattern = new KnowledgeNode(Guid.NewGuid(), KnowledgeNodeType.Pattern, "Add a log statement\nRun the linter", ["logging"], [], DateTimeOffset.UtcNow);
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient([pattern]), new CountingReasoningEngineClient("unused"));

        var tasks = await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        Assert.Equal(2, tasks.Length);
        Assert.Equal("Add a log statement", tasks[0].Description);
        Assert.Equal("Run the linter", tasks[1].Description);
    }

    [Fact]
    public async Task DecomposeAsync_ProducesASequentialDependencyChain_ForAMultiStepPattern()
    {
        var pattern = new KnowledgeNode(Guid.NewGuid(), KnowledgeNodeType.Pattern, "Step one\nStep two\nStep three", [], [], DateTimeOffset.UtcNow);
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient([pattern]), new CountingReasoningEngineClient("unused"));

        var tasks = await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        Assert.Equal(3, tasks.Length);
        Assert.Empty(tasks[0].DependsOnTaskIds);
        Assert.Equal([tasks[0].TaskId], tasks[1].DependsOnTaskIds);
        Assert.Equal([tasks[1].TaskId], tasks[2].DependsOnTaskIds);
    }

    [Fact]
    public async Task DecomposeAsync_IgnoresNonPatternNodes()
    {
        var fact = new KnowledgeNode(Guid.NewGuid(), KnowledgeNodeType.Fact, "Just a fact", ["logging"], [], DateTimeOffset.UtcNow);
        var builder = new TaskGraphBuilder(new FixedKnowledgeClient([fact]), new CountingReasoningEngineClient("unused"));

        var tasks = await builder.DecomposeAsync(CreateGoal(), CancellationToken.None);

        var task = Assert.Single(tasks);
        Assert.Equal("Add a logging statement to module X", task.Description);
    }
}
