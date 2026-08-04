namespace EOS.Planner.Tests;

public class PriorityManagerTests
{
    [Theory]
    [InlineData(0, 0.0, 0)]
    [InlineData(1, 0.0, 10)]
    [InlineData(3, 5.0, 25)]
    [InlineData(2, 2.4, 18)]
    public void ComputePriority_AppliesTheDocumentedFormula(int dependentGoalCount, double estimatedResourceCost, int expectedPriority)
    {
        var priorityManager = new PriorityManager();

        var priority = priorityManager.ComputePriority(dependentGoalCount, estimatedResourceCost);

        Assert.Equal(expectedPriority, priority);
    }

    [Fact]
    public void ComputePriority_FloorsAtZero_WhenResourceCostExceedsDependencyCriticality()
    {
        var priorityManager = new PriorityManager();

        var priority = priorityManager.ComputePriority(dependentGoalCount: 0, estimatedResourceCost: 100.0);

        Assert.Equal(0, priority);
    }
}
