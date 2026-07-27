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
}
