using EOS.Contracts;

namespace EOS.Planner.Tests;

public class GoalValidatorTests
{
    private static Goal CreateGoal(string statement = "Add a logging statement") =>
        new(Guid.NewGuid(), statement, null, ["logging"], "Product Owner", GoalLifecycleState.Proposed, null);

    [Fact]
    public void Validate_ReturnsFeasible_ForAWellFormedGoal_WhenProtectionAllows()
    {
        var validator = new GoalValidator(new AlwaysAllowProtectionClient(), new NoOpGoalValidatedEventPublisher());

        var result = validator.Validate(CreateGoal());

        Assert.True(result.Feasible);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReturnsNotFeasible_ForAnEmptyOrWhitespaceStatement(string statement)
    {
        var validator = new GoalValidator(new AlwaysAllowProtectionClient(), new NoOpGoalValidatedEventPublisher());

        var result = validator.Validate(CreateGoal(statement));

        Assert.False(result.Feasible);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Validate_ReturnsNotFeasible_WhenProtectionDenies()
    {
        var validator = new GoalValidator(new AlwaysDenyProtectionClient(), new NoOpGoalValidatedEventPublisher());

        var result = validator.Validate(CreateGoal());

        Assert.False(result.Feasible);
        Assert.Equal("Denied by test.", result.Reason);
    }

    [Fact]
    public void Validate_PublishesGoalValidated_WithTheCorrectFeasibilityResult()
    {
        var publisher = new CapturingGoalValidatedEventPublisher();
        var validator = new GoalValidator(new AlwaysAllowProtectionClient(), publisher);

        validator.Validate(CreateGoal());

        Assert.Equal(1, publisher.CallCount);
        Assert.True(publisher.LastFeasibilityResult);
    }

    private sealed class CapturingGoalValidatedEventPublisher : IGoalValidatedEventPublisher
    {
        public int CallCount { get; private set; }
        public bool LastFeasibilityResult { get; private set; }

        public void PublishGoalValidated(Guid goalId, bool feasibilityResult)
        {
            CallCount++;
            LastFeasibilityResult = feasibilityResult;
        }
    }
}
