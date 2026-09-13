using EOS.Contracts;
using EOS.Gates;

namespace EOS.Gates.Tests;

public class RuleEngineTests
{
    [Theory]
    [InlineData("Decision")]
    [InlineData("Production release")]
    [InlineData("AnyAction")]
    public void Evaluate_AlwaysAllows_ThisWP(string actionType)
    {
        var engine = new RuleEngine();

        var decision = engine.Evaluate(actionType);

        Assert.True(decision.Allow);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void EvaluateUniversalGates_Allows_WhenBuildAndTestsPass()
    {
        var decision = new RuleEngine().EvaluateUniversalGates(new UniversalGateResult(
            new GateStepResult(GateStepStatus.Passed, "Built"),
            new GateStepResult(GateStepStatus.Passed, "Tested")));

        Assert.True(decision.Allow);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void EvaluateUniversalGates_Allows_WhenBuildPassesAndNoTestProjectApplies()
    {
        var decision = new RuleEngine().EvaluateUniversalGates(new UniversalGateResult(
            new GateStepResult(GateStepStatus.Passed, "Built"),
            new GateStepResult(GateStepStatus.NotApplicable, "No test project")));

        Assert.True(decision.Allow);
    }

    [Fact]
    public void EvaluateUniversalGates_Denies_WhenBuildFails_AndNamesGate1()
    {
        var decision = new RuleEngine().EvaluateUniversalGates(new UniversalGateResult(
            new GateStepResult(GateStepStatus.Failed, "error CS1519"),
            new GateStepResult(GateStepStatus.NotApplicable, "Not run")));

        Assert.False(decision.Allow);
        Assert.Contains("Universal Gate 1", decision.Reason);
        Assert.Contains("CS1519", decision.Reason);
    }

    [Fact]
    public void EvaluateUniversalGates_Denies_WhenBuildDidNotRun_FailClosed()
    {
        var decision = new RuleEngine().EvaluateUniversalGates(new UniversalGateResult(
            new GateStepResult(GateStepStatus.NotApplicable, "toolchain missing"),
            new GateStepResult(GateStepStatus.NotApplicable, "Not run")));

        Assert.False(decision.Allow);
        Assert.Contains("Universal Gate 1", decision.Reason);
    }

    [Fact]
    public void EvaluateUniversalGates_Denies_WhenTestsFail_AndNamesGate2()
    {
        var decision = new RuleEngine().EvaluateUniversalGates(new UniversalGateResult(
            new GateStepResult(GateStepStatus.Passed, "Built"),
            new GateStepResult(GateStepStatus.Failed, "Failed! 1 test")));

        Assert.False(decision.Allow);
        Assert.Contains("Universal Gate 2", decision.Reason);
        Assert.Contains("Failed! 1 test", decision.Reason);
    }

    [Fact]
    public void EvaluateUniversalGates_Throws_OnNullResult()
    {
        Assert.Throws<ArgumentNullException>(() => new RuleEngine().EvaluateUniversalGates(null!));
    }
}
